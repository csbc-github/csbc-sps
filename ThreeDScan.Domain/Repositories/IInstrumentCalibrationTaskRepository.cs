using ThreeDScan.Domain.Models;

namespace ThreeDScan.Domain.Repositories;

/// <summary>UC-01 兩次拍攝任務暫存（InstrumentCalibrationTask）。</summary>
public interface IInstrumentCalibrationTaskRepository
{
    Task CreateAsync(InstrumentCalibrationTaskRecord task, CancellationToken ct = default);

    Task<InstrumentCalibrationTaskRecord?> GetAsync(string taskId, CancellationToken ct = default);

    Task CompleteAsync(string taskId, CancellationToken ct = default);
}
