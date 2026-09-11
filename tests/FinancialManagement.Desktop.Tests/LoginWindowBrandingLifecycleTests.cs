using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Resources;
using FinancialManagement.Application.Authentication;
using FinancialManagement.Desktop.ViewModels;
using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Desktop.Tests;

public sealed class LoginWindowBrandingLifecycleTests
{
    [Fact]
    public void Constructor_loads_and_decodes_login_branding_resources()
    {
        WpfTestHost.Invoke(() =>
        {
            var window = CreateWindow();
            try
            {
                Assert.Equal("Connexion — Tresoria", window.Title);
                Assert.NotNull(window.Icon);
                Assert.True(window.Icon.Width > 0);
                Assert.True(window.Icon.Height > 0);

                var brandIcon = Assert.IsType<Image>(window.FindName("LoginBrandIcon"));
                Assert.NotNull(brandIcon.Source);
                Assert.True(brandIcon.Source.Width > 0);
                Assert.True(brandIcon.Source.Height > 0);

                var card = Assert.IsType<Border>(window.FindName("LoginCard"));
                Assert.Equal(HorizontalAlignment.Center, card.HorizontalAlignment);
                Assert.Equal(VerticalAlignment.Center, card.VerticalAlignment);
                Assert.Equal(new CornerRadius(18), card.CornerRadius);
                Assert.IsType<DropShadowEffect>(card.Effect);

                var username = Assert.IsType<TextBox>(window.FindName("UsernameInput"));
                var password = Assert.IsType<PasswordBox>(window.FindName("PasswordInput"));
                Assert.Equal(0, username.TabIndex);
                Assert.Equal(1, password.TabIndex);

                AssertDecodes("Resources/Branding/TresoriaAppIcon.ico", minimumFrameCount: 7);
                AssertDecodes("Resources/Branding/TresoriaAppIcon.png", minimumFrameCount: 1);
                AssertDecodes("Resources/Branding/TresoriaSidebarLogo.png", minimumFrameCount: 1);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Constructor_rejects_missing_view_model_before_Xaml_initialization()
    {
        WpfTestHost.Invoke(() =>
        {
            var exception = Assert.Throws<ArgumentNullException>(() => new LoginWindow(null!));
            Assert.Equal("viewModel", exception.ParamName);
        });
    }

    [Fact]
    public void Closing_remains_cancelled_while_login_is_busy()
    {
        WpfTestHost.Invoke(() =>
        {
            var window = CreateWindow();
            var viewModel = Assert.IsType<LoginViewModel>(window.DataContext);
            SetBusy(viewModel, true);

            var closing = new CancelEventArgs();
            InvokeOnClosing(window, closing);
            Assert.True(closing.Cancel);

            SetBusy(viewModel, false);
            closing = new CancelEventArgs();
            InvokeOnClosing(window, closing);
            Assert.False(closing.Cancel);
            window.Close();
        });
    }

    private static LoginWindow CreateWindow() =>
        new(new LoginViewModel(new AuthenticationServiceStub(), new EmptySession()));

    private static void AssertDecodes(string resourcePath, int minimumFrameCount)
    {
        var uri = new Uri(
            $"/FinancialManagement.Desktop;component/{resourcePath}",
            UriKind.Relative);
        var resource = Assert.IsType<StreamResourceInfo>(System.Windows.Application.GetResourceStream(uri));
        using var stream = resource.Stream;
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);

        Assert.True(decoder.Frames.Count >= minimumFrameCount);
        Assert.All(decoder.Frames, frame =>
        {
            Assert.True(frame.PixelWidth > 0);
            Assert.True(frame.PixelHeight > 0);
        });
    }

    private static void SetBusy(LoginViewModel viewModel, bool value) =>
        typeof(LoginViewModel)
            .GetProperty(nameof(LoginViewModel.IsBusy), BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(viewModel, value);

    private static void InvokeOnClosing(LoginWindow window, CancelEventArgs args) =>
        typeof(LoginWindow)
            .GetMethod("OnClosing", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [args]);

    private sealed class AuthenticationServiceStub : IAuthenticationService
    {
        public Task<AuthenticationResult> AuthenticateAsync(
            string username,
            string password,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AuthenticationResult.Failed());

        public void Logout()
        {
        }
    }

    private sealed class EmptySession : ICurrentUserSession
    {
        public bool IsAuthenticated => false;
        public long? UserId => null;
        public string? FullName => null;
        public string? Username => null;
        public UserRole? Role => null;
    }
}
