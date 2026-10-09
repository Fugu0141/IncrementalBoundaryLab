namespace BoundaryLab.Core;

// Retains a common research/reporting contract while the old and newly
// designed engines remain independently selectable.
public interface IResearchSession
{
    EvidenceLatticeParameters Parameters { get; }
    bool MozcAvailable { get; }
    EvidenceLatticeResult Update(string input);
    void Reset();
}
