using Microsoft.Extensions.DependencyInjection;
using nexRemote.Desktop.Core;
using nexRemote.Desktop.Core.Services;
using nexRemote.Desktop.Core.ViewModels;
using nexRemote.Shared.Models;
using nexRemote.Shared.Utilities;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace nexRemote.Desktop.Win.ViewModels
{
    public class BrandedViewModelBase : ViewModelBase
    {
        public BrandedViewModelBase()
        {
            DeviceInitService = ServiceContainer.Instance?.GetRequiredService<IDeviceInitService>();

            ApplyBranding();
        }

        public void ApplyBranding()
        {
            try
            {
                var brandingInfo = DeviceInitService?.BrandingInfo ?? new BrandingInfo();

                ProductName = "nexRemote";

                if (!string.IsNullOrWhiteSpace(brandingInfo?.Product))
                {
                    ProductName = brandingInfo.Product;
                }

                TitleBackgroundColor = new SolidColorBrush(Color.FromRgb(
                    brandingInfo?.TitleBackgroundRed ?? 0,
                    brandingInfo?.TitleBackgroundGreen ?? 0,
                    brandingInfo?.TitleBackgroundBlue ?? 0));

                TitleForegroundColor = new SolidColorBrush(Color.FromRgb(
                   brandingInfo?.TitleForegroundRed ?? 0,
                   brandingInfo?.TitleForegroundGreen ?? 160,
                   brandingInfo?.TitleForegroundBlue ?? 227));

                TitleButtonForegroundColor = new SolidColorBrush(Color.FromRgb(
                   brandingInfo?.ButtonForegroundRed ?? 255,
                   brandingInfo?.ButtonForegroundGreen ?? 255,
                   brandingInfo?.ButtonForegroundBlue ?? 255));

                Icon = GetBitmapImageIcon(brandingInfo);

                FirePropertyChanged(nameof(ProductName));
                FirePropertyChanged(nameof(TitleBackgroundColor));
                FirePropertyChanged(nameof(TitleForegroundColor));
                FirePropertyChanged(nameof(TitleButtonForegroundColor));
                FirePropertyChanged(nameof(Icon));
            }
            catch (Exception ex)
            {
                Logger.Write(ex, "Błąd stosowania brandingu.");
            }
        }

        public BitmapImage Icon { get; set; }
        public string ProductName { get; set; }
        public SolidColorBrush TitleBackgroundColor { get; set; }
        public SolidColorBrush TitleButtonForegroundColor { get; set; }
        public SolidColorBrush TitleForegroundColor { get; set; }
        protected IDeviceInitService DeviceInitService { get; }
        private BitmapImage GetBitmapImageIcon(BrandingInfo bi)
        {
            Stream imageStream;
            if (bi.Icon?.Any() == true)
            {
                imageStream = new MemoryStream(bi.Icon);
            }
            else
            {
                imageStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("nexRemote.Desktop.Win.Assets.nexRemote_Icon.png");
            }

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = imageStream;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            imageStream.Close();

            return bitmap;
        }
    }

}
