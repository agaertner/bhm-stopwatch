using Blish_HUD.Graphics.UI;
using Nekres.Stopwatch.UI.Models;
using Nekres.Stopwatch.UI.Views;

namespace Nekres.Stopwatch.UI.Presenters
{
    public class CustomSettingsPresenter : Presenter<CustomSettingsView, CustomSettingsModel>
    {
        public CustomSettingsPresenter(CustomSettingsView view, CustomSettingsModel model) : base(view, model) {}
    }
}
