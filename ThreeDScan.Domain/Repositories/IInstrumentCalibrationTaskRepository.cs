using ThreeDScan.Domain.Models;

namespace ThreeDScan.Domain.Repositories;

/// <summary>UC-01 兩次拍攝任務暫存（InstrumentCalibrationTask）。</summary>
public interface IInstrumentCalibrationTaskRepository
{
    Task CreateAsync(InstrumentCalibrationTaskRecord task, CancellationToken ct = default);

    Task<InstrumentCalibrationTaskRecord?> GetAsync(string taskId, CancellationToken ct = default);

    /// <summary>尚未結束的第一次拍攝任務。</summary>
    Task<IReadOnlyList<InstrumentCalibrationTaskRecord>> ListPendingAsync(CancellationToken ct = default);

    Task CompleteAsync(string taskId, CancellationToken ct = default);

    /// <summary>Watchdog：標記為已逾時，不可再 complete。</summary>
    Task ExpireAsync(string taskId, CancellationToken ct = default);
}
