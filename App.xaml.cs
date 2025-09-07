using System.Configuration;
using System.Data;
using System.Windows;
using Syncfusion.Licensing;

namespace MGK_Analyzer
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Syncfusion Community License 등록
            SyncfusionLicenseProvider.RegisterLicense("Ngo9BigBOggjHTQxAR8/V1JFaF5cXGRCf1FpRmJGdld5fUVHYVZUTXxaS00DNHVRdkdmWXZed3RQQmBdUkJ+XEBWYEg=");
            
            base.OnStartup(e);
        }
    }

}
