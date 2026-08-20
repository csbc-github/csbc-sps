using ThreeDScan.Domain.Models;

namespace ThreeDScan.Domain.Abstractions;

/// <summary>UC-01 兩次拍攝任務暫存（InstrumentCalibrationTask）。</summary>
public interface IInstrumentCalibrationTaskStore
{
    Task CreateAsync(InstrumentCalibrationTaskRecord task, CancellationToken ct = default);

    Task<InstrumentCalibrationTaskRecord?> GetAsync(string taskId, CancellationToken ct = default);

    Task CompleteAsync(string taskId, CancellationToken ct = default);
}
