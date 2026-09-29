using Microsoft.Extensions.Configuration;

namespace ThreeDScan.ViewModels;

// 割切區 VM：對應 SA UC-04 面掃描（割切區）／ SD §10.4
public class CuttingViewModel : SurfaceScanAreaViewModel
{
    public CuttingViewModel()
        : this(AppConfiguration.Load())
    {
    }

    public CuttingViewModel(IConfiguration config)
        : base(config, "cutting")
    {
    }
}
