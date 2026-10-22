#!/usr/bin/env bash
# ntfs-test-matrix.sh - Linux counterpart of New-NtfsTestMatrix.ps1
#
# Builds sparse image files, formats each with mkntfs using a different logical
# sector size / cluster size, fills them via ntfs-3g, writes a SHA-256 manifest
# (the oracle), unmounts, and optionally runs your parser against each volume.
#
# Needs root, plus: mkntfs + ntfs-3g (package ntfs-3g / ntfsprogs), losetup, od,
# sha256sum, setfattr (attr), sfdisk (only with --gpt).
#
# IMPORTANT LIMITS: these volumes are formatted by mkntfs and written by ntfs-3g,
# NOT by Windows. There is no EFS, no $UsnJrnl, no 4 KiB file records, and the
# $Secure / compression / reparse data are ntfs-3g flavoured. Use this for fast
# geometry coverage (sector x cluster) and CI; still test real Windows volumes.

set -u -o pipefail

usage() {
cat <<'EOF'
Usage: sudo ./ntfs-test-matrix.sh [options]

  --dir DIR              Work directory for images + logs (default ./ntfs-tests)
  --image FILE           Test ONE existing image/device instead of the matrix.
                         Never reformatted or populated (mounted read-only for the manifest).
  --offset BYTES         Partition offset inside --image (default 0)
  --sector-sizes LIST    Logical sector sizes, comma separated (default 512,4096)
  --cluster-sizes LIST   Cluster sizes (default 512,1024,2048,4096,8192,16384,32768,65536)
  --size SIZE            Image size, truncate syntax (default 2G)
  --gpt                  Put a GPT in front; the volume starts at 1 MiB (offset 1048576)
  --no-populate          Leave volumes empty
  --large-dir N          Files in the big directory (default 3000)
  --small-files N        MFT churn file count (default 500)
  --frag-files N         Interleaved fragmented files (default 12)
  --frag-mb N            Size of each fragmented file in MB (default 8)
  --parser CMD           Command run per volume; exit code 0 = pass. Placeholders:
                           {Device} {Image} {Offset} {Manifest} {LogDir} {Name}
                           {SectorSize} {ClusterSize} {FrsSize}
                         {Device} is a loop device whose offset 0 is the NTFS boot sector
                         and is attached only while the parser runs.
  --filter GLOB          Only run combos whose name matches (e.g. '*cl65536*')
  --list-only            Print the plan and exit
  --keep                 Keep images of passing volumes too
  --report FILE          CSV report (default DIR/report.csv)
  -h, --help

Example:
  sudo ./ntfs-test-matrix.sh --gpt --parser 'dotnet run --project ~/Ntfs4Testing -- --device {Device} --manifest {Manifest}'
EOF
}

DIR="./ntfs-tests"; IMAGE=""; OFFSET=0
SECTORS="512,4096"; CLUSTERS="512,1024,2048,4096,8192,16384,32768,65536"
SIZE="2G"; GPT=0; POPULATE=1
LARGE_DIR=3000; SMALL=500; FRAG_FILES=12; FRAG_MB=8
PARSER=""; FILTER="*"; LIST_ONLY=0; KEEP=0; REPORT=""

while [ $# -gt 0 ]; do
  case "$1" in
    --dir) DIR="$2"; shift 2 ;;
    --image) IMAGE="$2"; shift 2 ;;
    --offset) OFFSET="$2"; shift 2 ;;
    --sector-sizes) SECTORS="$2"; shift 2 ;;
    --cluster-sizes) CLUSTERS="$2"; shift 2 ;;
    --size) SIZE="$2"; shift 2 ;;
    --gpt) GPT=1; shift ;;
    --no-populate) POPULATE=0; shift ;;
    --large-dir) LARGE_DIR="$2"; shift 2 ;;
    --small-files) SMALL="$2"; shift 2 ;;
    --frag-files) FRAG_FILES="$2"; shift 2 ;;
    --frag-mb) FRAG_MB="$2"; shift 2 ;;
    --parser) PARSER="$2"; shift 2 ;;
    --filter) FILTER="$2"; shift 2 ;;
    --list-only) LIST_ONLY=1; shift ;;
    --keep) KEEP=1; shift ;;
    --report) REPORT="$2"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown option: $1" >&2; usage; exit 2 ;;
  esac
done

[ -n "$REPORT" ] || REPORT="$DIR/report.csv"
GPT_OFFSET=1048576

log() { # level message
  local line
  line="$(date +%H:%M:%S) [$1] $2"
  echo "$line"
  [ -n "${CURRENT_LOG:-}" ] && echo "$line" >> "$CURRENT_LOG"
  return 0
}

need() {
  command -v "$1" >/dev/null 2>&1 || { echo "Missing required tool: $1" >&2; MISSING=1; }
}

MISSING=0
if [ "$LIST_ONLY" -eq 0 ]; then
  [ "$(id -u)" -eq 0 ] || { echo "Run as root (loop devices + mounting)." >&2; exit 2; }
  for t in losetup od sha256sum truncate; do need "$t"; done
  if [ -z "$IMAGE" ]; then need mkntfs; fi
  need ntfs-3g
  [ "$POPULATE" -eq 1 ] && need setfattr
  [ "$GPT" -eq 1 ] && need sfdisk
  [ "$MISSING" -eq 0 ] || exit 2
fi

# ------------------------------------------------------------------ helpers
LOOPS=()
LOOPDEV=""
MNT=""
cleanup() {
  [ -n "$MNT" ] && mountpoint -q "$MNT" 2>/dev/null && umount "$MNT" 2>/dev/null
  local d
  for d in "${LOOPS[@]:-}"; do [ -n "$d" ] && losetup -d "$d" 2>/dev/null; done
  LOOPS=()
}
trap cleanup EXIT INT TERM

attach_loop() { # image sectorsize offset
  # Sets LOOPDEV (not a subshell, so LOOPS is tracked for cleanup).
  if [ "$3" -gt 0 ]; then
    LOOPDEV=$(losetup --find --show -b "$2" -o "$3" "$1") || return 1
  else
    LOOPDEV=$(losetup --find --show -b "$2" "$1") || return 1
  fi
  LOOPS+=("$LOOPDEV")
}

detach_all() {
  local d
  for d in "${LOOPS[@]:-}"; do [ -n "$d" ] && losetup -d "$d" 2>/dev/null; done
  LOOPS=()
}

# bytes-per-sector, bytes-per-cluster, MFT record size read straight from the boot sector
boot_geometry() { # device
  local dev="$1" bps spc raw
  bps=$(od -An -t u2 -j 11 -N 2 "$dev" | tr -d ' ')
  spc=$(od -An -t u1 -j 13 -N 1 "$dev" | tr -d ' ')
  raw=$(od -An -t u1 -j 64 -N 1 "$dev" | tr -d ' ')
  local cluster=$((bps * spc)) frs
  if [ "$raw" -gt 127 ]; then frs=$((1 << (256 - raw))); else frs=$((raw * cluster)); fi
  echo "$bps $cluster $frs"
}

csv_escape() { local s="${1//\"/\"\"}"; printf '"%s"' "$s"; }

write_manifest() { # root outfile
  local root="$1" out="$2" rel type size hash isdir
  echo "Path,IsDirectory,Length,Sha256" > "$out"
  local n=0
  while IFS= read -r -d '' rel; do
    rel="${rel#./}"
    [ -n "$rel" ] || continue
    case "$rel" in "System Volume Information"*|'$RECYCLE.BIN'*|lost+found*) continue ;; esac
    if [ -d "$root/$rel" ] && [ ! -L "$root/$rel" ]; then
      printf '%s,true,0,\n' "$(csv_escape "/$rel")" >> "$out"
    elif [ -L "$root/$rel" ]; then
      printf '%s,false,0,SYMLINK\n' "$(csv_escape "/$rel")" >> "$out"
    else
      size=$(stat -c %s "$root/$rel")
      hash=$(sha256sum "$root/$rel" 2>/dev/null | cut -d' ' -f1)
      [ -n "$hash" ] || hash="UNREADABLE"
      printf '%s,false,%s,%s\n' "$(csv_escape "/$rel")" "$size" "$hash" >> "$out"
    fi
    n=$((n + 1))
  done < <(cd "$root" && find . -mindepth 1 -print0 | LC_ALL=C sort -z)
  echo "$n"
}

rand_file() { # path bytes
  head -c "$2" /dev/urandom > "$1"
}

populate() { # mountpoint cluster
  local r="$1" cluster="$2" i
  log INFO "Populate: large directory ($LARGE_DIR files)"
  mkdir -p "$r/largedir"
  for ((i = 0; i < LARGE_DIR; i++)); do
    printf 'content %d' "$i" > "$(printf '%s/largedir/file_%06d_with_a_fairly_long_name_to_fill_index_nodes.txt' "$r" "$i")"
  done

  log INFO "Populate: unicode / collation names"
  mkdir -p "$r/unicode"
  local n
  for n in a B _underscore '~tilde' 0digit Zebra zebra2 $'caf\xc3\xa9' $'CAF\xc3\x89 2' \
           $'\xce\xa9\xcf\x89_greek' $'\xe6\x97\xa5\xe6\x9c\xac\xe8\xaa\x9e' \
           $'\xd0\x9f\xd1\x80\xd0\xb8\xd0\xb2\xd0\xb5\xd1\x82' $'emoji_\xf0\x9f\x98\x80' \
           'with space' 'a.b.c.d.e' "$(printf 'x%.0s' {1..200})"; do
    printf 'u' > "$r/unicode/$n" 2>/dev/null || log WARN "could not create name: $n"
  done
  for c in {a..z}; do
    u=$(echo "$c" | tr a-z A-Z)
    printf 'm' > "$r/unicode/Mix${c}${u}"
  done

  log INFO "Populate: MFT churn (create + delete)"
  mkdir -p "$r/churn"
  for ((i = 0; i < SMALL * 2; i++)); do
    head -c $(((i % 700) + 1)) /dev/zero | tr '\0' x > "$r/churn/c$i.tmp"
  done
  for ((i = 0; i < SMALL * 2; i += 2)); do rm -f "$r/churn/c$i.tmp"; done
  for ((i = 0; i < SMALL; i++)); do printf 'reuse %d' "$i" > "$r/churn/r$i.tmp"; done

  log INFO "Populate: resident vs non-resident boundary sizes"
  mkdir -p "$r/sizes"
  local sz
  for sz in 0 1 100 500 600 700 900 1000 1500 3000 4095 4096 4097 65535 65536 65537 1048576; do
    rand_file "$r/sizes/size_$sz.bin" "$sz"
  done

  log INFO "Populate: fragmented files ($FRAG_FILES x ${FRAG_MB} MB, interleaved)"
  mkdir -p "$r/fragmented"
  for ((i = 0; i < FRAG_FILES; i++)); do : > "$r/fragmented/frag_$i.bin"; done
  local rounds=$((FRAG_MB * 16)) k  # 64 KiB chunks
  for ((k = 0; k < rounds; k++)); do
    for ((i = 0; i < FRAG_FILES; i++)); do
      head -c 65536 /dev/urandom >> "$r/fragmented/frag_$i.bin"
    done
  done

  log INFO "Populate: sparse files"
  mkdir -p "$r/sparse"
  truncate -s 256M "$r/sparse/sparse_big.bin"
  head -c 200 /dev/urandom | dd of="$r/sparse/sparse_big.bin" bs=1 seek=0 conv=notrunc status=none
  head -c 200 /dev/urandom | dd of="$r/sparse/sparse_big.bin" bs=1M seek=100 conv=notrunc status=none
  head -c 200 /dev/urandom | dd of="$r/sparse/sparse_big.bin" bs=1 seek=$((256 * 1048576 - 200)) conv=notrunc status=none
  truncate -s 64M "$r/sparse/sparse_all_holes.bin"

  if [ "$cluster" -le 4096 ]; then
    log INFO "Populate: ntfs-3g compressed files (flag set before data is written)"
    mkdir -p "$r/compressed"
    local text f
    text=$(printf 'The quick brown fox jumps over the lazy dog. %.0s' {1..4000})
    for ((i = 0; i < 6; i++)); do
      f="$r/compressed/text_$i.txt"
      : > "$f"
      setfattr -h -n system.ntfs_attrib_be -v 0x00000800 "$f" 2>/dev/null || { log WARN "cannot set compressed flag"; break; }
      for ((k = 0; k <= i; k++)); do printf '%s' "$text" >> "$f"; done
    done
    f="$r/compressed/incompressible.bin"; : > "$f"
    setfattr -h -n system.ntfs_attrib_be -v 0x00000800 "$f" 2>/dev/null
    rand_file "$f" 3145728
  else
    log WARN "Skipping compression (cluster > 4096)"
  fi

  log INFO "Populate: links and alternate data streams"
  mkdir -p "$r/links/subdir"
  printf 'link target' > "$r/links/target.txt"
  printf 'inner' > "$r/links/subdir/inner.txt"
  ln "$r/links/target.txt" "$r/links/hardlink1.txt"
  ln "$r/links/target.txt" "$r/links/hardlink2.txt"
  ln -s target.txt "$r/links/sym_rel_file.txt"
  ln -s subdir "$r/links/sym_rel_dir"
  ln -s ../sizes "$r/links/sym_up"
  ln -s sym_rel_file.txt "$r/links/sym_chain1.txt"
  ln -s sym_chain1.txt "$r/links/sym_chain2.txt"
  ln -s "$r/links/target.txt" "$r/links/sym_abs_file.txt"
  setfattr -n user.ads1 -v "alternate stream one" "$r/links/target.txt" 2>/dev/null || log WARN "ADS (xattr streams) unavailable"

  log INFO "Populate: many hard links"
  mkdir -p "$r/manylinks"
  printf 'base' > "$r/manylinks/base.txt"
  for ((i = 0; i < 150; i++)); do
    ln "$r/manylinks/base.txt" "$r/manylinks/link_with_a_reasonably_long_name_$i.txt"
  done
}

# ------------------------------------------------------------------ plan
declare -a PLAN_NAMES PLAN_SECTOR PLAN_CLUSTER
IFS=',' read -r -a SECTOR_LIST <<< "$SECTORS"
IFS=',' read -r -a CLUSTER_LIST <<< "$CLUSTERS"

if [ -n "$IMAGE" ]; then
  PLAN_NAMES+=("$(basename "$IMAGE")"); PLAN_SECTOR+=(0); PLAN_CLUSTER+=(0)
else
  for s in "${SECTOR_LIST[@]}"; do
    for c in "${CLUSTER_LIST[@]}"; do
      [ "$c" -lt "$s" ] && continue
      name="ls${s}-cl${c}"
      # shellcheck disable=SC2254
      case "$name" in $FILTER) ;; *) continue ;; esac
      PLAN_NAMES+=("$name"); PLAN_SECTOR+=("$s"); PLAN_CLUSTER+=("$c")
    done
  done
fi

echo "Planned volumes: ${#PLAN_NAMES[@]}"
printf '  %s\n' "${PLAN_NAMES[@]:-}"
[ "$LIST_ONLY" -eq 1 ] && exit 0
[ "${#PLAN_NAMES[@]}" -gt 0 ] || { echo "Nothing to do."; exit 0; }

mkdir -p "$DIR/logs" "$(dirname "$REPORT")"
echo "Name,Status,ReqSector,ReqCluster,ActSector,ActCluster,ActFrs,Files,ParserExit,Notes,Image" > "$REPORT"

# ------------------------------------------------------------------ run
FAILED=0
for idx in "${!PLAN_NAMES[@]}"; do
  name="${PLAN_NAMES[$idx]}"; rs="${PLAN_SECTOR[$idx]}"; rc="${PLAN_CLUSTER[$idx]}"
  logdir="$DIR/logs/$name"; mkdir -p "$logdir"
  CURRENT_LOG="$logdir/run.log"; : > "$CURRENT_LOG"
  log INFO "===== $name ====="

  notes=(); status="PASS"; parser_exit=""; files=0
  as=0; ac=0; af=0
  generated=1; img="$DIR/$name.img"; off=0
  if [ -n "$IMAGE" ]; then generated=0; img="$IMAGE"; off="$OFFSET"; rs=$(od -An -t u2 -j 11 -N 2 "$IMAGE" | tr -d ' '); fi

  # ---- create + format
  if [ "$generated" -eq 1 ]; then
    rm -f "$img"; truncate -s "$SIZE" "$img" || { notes+=("truncate failed"); status="ERROR"; }
    if [ "$status" != "ERROR" ]; then
      if [ "$GPT" -eq 1 ]; then
        off=$GPT_OFFSET
        attach_loop "$img" "$rs" 0 && whole=$LOOPDEV || { notes+=("losetup failed"); status="ERROR"; }
        if [ "$status" != "ERROR" ]; then
          printf 'label: gpt\nstart=%d, type=EBD0A0A2-B9E5-4433-87C0-68B6B72699C7\n' $((GPT_OFFSET / rs)) \
            | sfdisk -q "$whole" >>"$CURRENT_LOG" 2>&1 || { notes+=("sfdisk failed"); status="ERROR"; }
          detach_all
        fi
      fi
    fi
    if [ "$status" != "ERROR" ]; then
      attach_loop "$img" "$rs" "$off" && dev=$LOOPDEV || { notes+=("losetup failed"); status="ERROR"; }
    fi
    if [ "$status" != "ERROR" ]; then
      log INFO "mkntfs -c $rc -s $rs on $dev"
      mkntfs -Q -F -c "$rc" -s "$rs" -L "$name" "$dev" >"$logdir/mkntfs.txt" 2>&1 \
        || { notes+=("mkntfs failed (see mkntfs.txt)"); status="ERROR"; }
    fi
  else
    attach_loop "$img" "$rs" "$off" && dev=$LOOPDEV || { notes+=("losetup failed"); status="ERROR"; }
  fi

  # ---- geometry read-back, populate, manifest
  if [ "$status" != "ERROR" ]; then
    read -r as ac af <<< "$(boot_geometry "$dev")"
    log INFO "Actual geometry (boot sector): sector=$as cluster=$ac frs=$af"
    if [ "$generated" -eq 1 ]; then
      [ "$as" -eq "$rs" ] || notes+=("sector mismatch: wanted $rs got $as")
      [ "$ac" -eq "$rc" ] || notes+=("cluster mismatch: wanted $rc got $ac")
    fi

    MNT=$(mktemp -d)
    mopts="rw"; [ "$generated" -eq 0 ] && mopts="ro"
    if ntfs-3g -o "$mopts,streams_interface=xattr" "$dev" "$MNT" >"$logdir/mount.txt" 2>&1; then
      if [ "$generated" -eq 1 ] && [ "$POPULATE" -eq 1 ]; then
        populate "$MNT" "$ac" >>"$CURRENT_LOG" 2>&1
        sync
      fi
      log INFO "Writing manifest"
      files=$(write_manifest "$MNT" "$logdir/manifest.csv")
      umount "$MNT" || { sleep 1; umount "$MNT" || notes+=("umount failed"); }
    else
      notes+=("ntfs-3g mount failed (see mount.txt)")
    fi
    rmdir "$MNT" 2>/dev/null; MNT=""

    if command -v ntfsfix >/dev/null 2>&1; then
      ntfsfix -n "$dev" >"$logdir/ntfsfix.txt" 2>&1 || notes+=("ntfsfix -n reported problems")
    fi

    # ---- parser
    if [ -n "$PARSER" ]; then
      cmd="$PARSER"
      for kv in "Device=$dev" "Image=$img" "Offset=$off" "Manifest=$logdir/manifest.csv" "LogDir=$logdir" \
                "Name=$name" "SectorSize=$as" "ClusterSize=$ac" "FrsSize=$af"; do
        cmd="${cmd//\{${kv%%=*}\}/${kv#*=}}"
      done
      log INFO "Parser: $cmd"
      bash -c "$cmd" >"$logdir/parser.txt" 2>"$logdir/parser.err"
      parser_exit=$?
      [ "$parser_exit" -eq 0 ] || notes+=("parser exit $parser_exit")
    fi
  fi

  detach_all
  [ "${#notes[@]}" -eq 0 ] || { [ "$status" = "ERROR" ] || status="FAIL"; }
  if [ "$status" = "PASS" ]; then
    log PASS "$name"
    [ "$generated" -eq 1 ] && [ "$KEEP" -eq 0 ] && { rm -f "$img"; img="(deleted)"; }
  else
    log FAIL "$name: ${notes[*]:-}"
    FAILED=$((FAILED + 1))
  fi

  printf '%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s\n' "$name" "$status" "$rs" "$rc" "$as" "$ac" "$af" "$files" \
    "$parser_exit" "$(csv_escape "${notes[*]:-}")" "$(csv_escape "$img")" >> "$REPORT"
done

CURRENT_LOG=""
echo
column -s, -t "$REPORT" 2>/dev/null | cut -c1-160 || cat "$REPORT"
echo
echo "Report: $REPORT"
echo "$(( ${#PLAN_NAMES[@]} - FAILED )) of ${#PLAN_NAMES[@]} volumes passed."
[ "$FAILED" -eq 0 ]
