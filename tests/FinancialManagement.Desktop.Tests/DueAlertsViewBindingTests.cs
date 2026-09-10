using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using FinancialManagement.Application.Alerts;
using FinancialManagement.Desktop.ViewModels;
using FinancialManagement.Desktop.Views;

namespace FinancialManagement.Desktop.Tests;

public sealed class DueAlertsViewBindingTests
{
    [Fact]
    public void NonEmptyReadonlyAlertRows_RenderWithoutDispatcherBindingExceptions()
    {
        WpfTestHost.Invoke(() =>
        {
            var app = System.Windows.Application.Current;
            Exception? dispatcherFailure = null;
            DispatcherUnhandledExceptionEventHandler handler = (_, args) =>
            {
                dispatcherFailure ??= args.Exception;
                args.Handled = true;
            };
            app.DispatcherUnhandledException += handler;
            try
            {
                var customerAlert = CreateAlert(
                    FinancialDueAlertSourceType.Sale,
                    FinancialDueAlertSide.CustomerReceivable,
                    "VTE-20260903-F4BF33F6E070",
                    "Client Atlas");
                var supplierAlert = CreateAlert(
                    FinancialDueAlertSourceType.Purchase,
                    FinancialDueAlertSide.SupplierPayable,
                    "A-2026-0068",
                    "Fournisseur Rif");
                var view = new DueAlertsView
                {
                    DataContext = new UatLikeAlertsModel(customerAlert, supplierAlert)
                };
                view.Measure(new Size(900, 600));
                view.Arrange(new Rect(0, 0, 900, 600));
                view.UpdateLayout();

                Assert.Null(dispatcherFailure);
                Assert.Equal("VTE-20260903-F4BF33F6E070", customerAlert.Reference);
                Assert.Contains("… · 10/09/2026", customerAlert.ReferenceAndDueDate);
            }
            finally
            {
                app.DispatcherUnhandledException -= handler;
            }
        });
    }

    private static DueAlertItemViewModel CreateAlert(
        FinancialDueAlertSourceType sourceType,
        FinancialDueAlertSide side,
        string reference,
        string partyName) =>
        new(new FinancialDueAlert(
            sourceType,
            side,
            1,
            reference,
            1,
            partyName,
            new DateOnly(2026, 9, 10),
            125_000,
            0,
            FinancialDueAlertStatus.DueToday,
            250_000));

    private sealed class UatLikeAlertsModel
    {
        public UatLikeAlertsModel(DueAlertItemViewModel customerAlert, DueAlertItemViewModel supplierAlert)
        {
            CustomerAlerts = [customerAlert];
            SupplierAlerts = [supplierAlert];
        }

        public ObservableCollection<DueAlertItemViewModel> CustomerAlerts { get; }

        public ObservableCollection<DueAlertItemViewModel> SupplierAlerts { get; }

        public string CurrentDateDescription => "10 septembre 2026";

        public bool HasData => true;

        public bool HasAlerts => true;

        public bool IsLoading => false;

        public bool HasError => false;

        public string? ErrorMessage => null;

        public string OverdueSummary => "0 échéance en retard";

        public string DueTodaySummary => "2 échéances aujourd'hui";

        public string UpcomingSummary => "0 échéance à venir";

        public string CustomerSummary => "Clients — 1 alerte, dont 0 en retard";

        public string SupplierSummary => "Fournisseurs — 1 alerte, dont 0 en retard";
    }
}
