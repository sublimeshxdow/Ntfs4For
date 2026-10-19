using System;
using System.Collections.Generic;
using System.Text;

namespace DeletedFileRecovery
{
    /// <summary>
    /// How much of a deleted file's content can still be trusted.
    /// </summary>
    internal enum Recoverability
    {
        /// <summary>Content lives inside the MFT record itself - always recoverable while the record survives.</summary>
        Resident,
        /// <summary>None of the file's clusters have been handed to another file since.</summary>
        Intact,
        /// <summary>Some clusters are in use again - parts of the output will contain other data.</summary>
        Partial,
        /// <summary>Every cluster is in use again - the content is gone.</summary>
        Overwritten,
        /// <summary>The record could not be parsed (e.g. its extension records were reused).</summary>
        Unreadable
    }
}
