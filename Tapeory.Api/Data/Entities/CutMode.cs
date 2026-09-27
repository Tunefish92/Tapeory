namespace Tapeory.Api.Data.Entities;

/// <summary>How the printer's cutter handles a job's labels.</summary>
public enum CutMode
{
    /// <summary>Cut each label off completely.</summary>
    AutoCut = 0,

    /// <summary>Cut only through the label layer between labels, leaving the backing intact so
    /// they stay together and peel easily; the whole strip is cut off after the last one.</summary>
    HalfCut = 1,

    /// <summary>No cuts between labels; one cut after the last.</summary>
    CutAtEnd = 2,

    /// <summary>Cut between labels, but don't feed and cut the last one. It stays in the printer
    /// until the next job (or the feed button) pushes it out, which saves the blank leader the
    /// printer otherwise feeds at the start of every job.</summary>
    ChainPrinting = 3
}
