using Microsoft.Extensions.Configuration;

namespace ThreeDScan.ViewModels;

// 噴印區 VM：對應 SA UC-04 面掃描（噴印區）／ SD §10.3
public class PrintingViewModel : SurfaceScanAreaViewModel
{
    public PrintingViewModel()
        : this(AppConfiguration.Load())
    {
    }

    public PrintingViewModel(IConfiguration config)
        : base(config, "printing")
    {
    }
}
