namespace ThreeDScan.Models;

// 依 SA NFR-U-01：關鍵狀態告警採紅／黃／綠三色區分
public enum SystemStatus
{
    Normal,   // 綠：系統就緒／運行正常
    Warning,  // 黃：警告（例如部分掃描器離線重試中）
    Error     // 紅：異常（例如掃描器離線、儀器校正失敗）
}

// 進料區角鋼掃描狀態（對應 SA UC-03 線掃描）
public enum ScanStatus
{
    Waiting,    // 等待掃描
    Scanning,   // 掃描中
    Completed,  // 掃描完成
    Error       // 掃描錯誤
}
