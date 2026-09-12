using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FinancialManagement.Application.Authentication;
using FinancialManagement.Desktop.ViewModels;
using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Desktop.Tests;

public sealed class LoginPasswordVisibilityTests
{
    [Fact]
    public void Password_is_hidden_by_default_and_eye_control_exists()
    {
        WpfTestHost.Invoke(() =>
        {
            var window = CreateWindow();
            try
            {
                var password = GetPasswordInput(window);
                var visiblePassword = GetVisiblePasswordInput(window);
                var toggle = GetVisibilityToggle(window);

                Assert.Equal(Visibility.Visible, password.Visibility);
                Assert.Equal(Visibility.Collapsed, visiblePassword.Visibility);
                Assert.False(toggle.IsChecked);
                Assert.True(toggle.Focusable);
                Assert.True(toggle.IsTabStop);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Toggle_reveals_exact_current_password()
    {
        WpfTestHost.Invoke(() =>
        {
            var window = CreateWindow();
            try
            {
                GetPasswordInput(window).Password = "Mot-de-passe! 42";

                GetVisibilityToggle(window).IsChecked = true;

                Assert.Equal(Visibility.Collapsed, GetPasswordInput(window).Visibility);
                Assert.Equal(Visibility.Visible, GetVisiblePasswordInput(window).Visibility);
                Assert.Equal("Mot-de-passe! 42", GetVisiblePasswordInput(window).Text);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Toggle_back_preserves_exact_edited_password_and_clears_visible_copy()
    {
        WpfTestHost.Invoke(() =>
        {
            var window = CreateWindow();
            try
            {
                GetPasswordInput(window).Password = "initial";
                GetVisibilityToggle(window).IsChecked = true;
                GetVisiblePasswordInput(window).Text = "modifie-É9!";

                GetVisibilityToggle(window).IsChecked = false;

                Assert.Equal("modifie-É9!", GetPasswordInput(window).Password);
                Assert.Equal(string.Empty, GetVisiblePasswordInput(window).Text);
                Assert.Equal(Visibility.Visible, GetPasswordInput(window).Visibility);
                Assert.Equal(Visibility.Collapsed, GetVisiblePasswordInput(window).Visibility);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Repeated_toggles_preserve_password_without_duplication()
    {
        WpfTestHost.Invoke(() =>
        {
            var window = CreateWindow();
            try
            {
                const string expected = "abcABC-123!?";
                GetPasswordInput(window).Password = expected;

                for (var index = 0; index < 5; index++)
                {
                    GetVisibilityToggle(window).IsChecked = true;
                    Assert.Equal(expected, GetVisiblePasswordInput(window).Text);
                    GetVisibilityToggle(window).IsChecked = false;
                    Assert.Equal(expected, GetPasswordInput(window).Password);
                }
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Login_uses_current_password_from_active_representation(bool visible)
    {
        WpfTestHost.Invoke(() =>
        {
            var authentication = new RecordingAuthenticationService(AuthenticationResult.Failed());
            var window = CreateWindow(authentication);
            try
            {
                GetViewModel(window).Username = "responsable";
                GetPasswordInput(window).Password = "hidden-value";

                if (visible)
                {
                    GetVisibilityToggle(window).IsChecked = true;
                    GetVisiblePasswordInput(window).Text = "visible-value";
                }

                var result = window.AuthenticateCurrentPasswordAsync().GetAwaiter().GetResult();

                Assert.False(result);
                Assert.Equal(visible ? "visible-value" : "hidden-value", authentication.LastPassword);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Failed_authentication_clears_both_password_representations()
    {
        WpfTestHost.Invoke(() =>
        {
            var authentication = new RecordingAuthenticationService(AuthenticationResult.Failed());
            var window = CreateWindow(authentication);
            try
            {
                GetViewModel(window).Username = "responsable";
                GetPasswordInput(window).Password = "temporary";
                GetVisibilityToggle(window).IsChecked = true;

                var result = window.AuthenticateCurrentPasswordAsync().GetAwaiter().GetResult();

                Assert.False(result);
                AssertPasswordClearedAndHidden(window);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Successful_authentication_clears_both_password_representations()
    {
        WpfTestHost.Invoke(() =>
        {
            var user = new AuthenticatedUser(7, "Responsable", "responsable", UserRole.Responsable);
            var authentication = new RecordingAuthenticationService(AuthenticationResult.Succeeded(user));
            var window = CreateWindow(authentication, new AuthenticatedSession(user));
            try
            {
                GetViewModel(window).Username = user.Username;
                GetPasswordInput(window).Password = "temporary";
                GetVisibilityToggle(window).IsChecked = true;

                var result = window.AuthenticateCurrentPasswordAsync().GetAwaiter().GetResult();

                Assert.True(result);
                AssertPasswordClearedAndHidden(window);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Blank_password_is_not_submitted_and_both_representations_remain_clear()
    {
        WpfTestHost.Invoke(() =>
        {
            var authentication = new RecordingAuthenticationService(AuthenticationResult.Failed());
            var window = CreateWindow(authentication);
            try
            {
                GetViewModel(window).Username = "responsable";
                GetVisibilityToggle(window).IsChecked = true;

                var result = window.AuthenticateCurrentPasswordAsync().GetAwaiter().GetResult();

                Assert.False(result);
                Assert.Equal(0, authentication.CallCount);
                AssertPasswordClearedAndHidden(window);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Toggle_accessible_name_tracks_show_and_hide_action()
    {
        WpfTestHost.Invoke(() =>
        {
            var window = CreateWindow();
            try
            {
                var toggle = GetVisibilityToggle(window);
                Assert.Equal("Afficher le mot de passe", AutomationProperties.GetName(toggle));
                Assert.Equal("Afficher le mot de passe", toggle.ToolTip);

                toggle.IsChecked = true;
                Assert.Equal("Masquer le mot de passe", AutomationProperties.GetName(toggle));
                Assert.Equal("Masquer le mot de passe", toggle.ToolTip);

                toggle.IsChecked = false;
                Assert.Equal("Afficher le mot de passe", AutomationProperties.GetName(toggle));
                Assert.Equal("Afficher le mot de passe", toggle.ToolTip);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Tab_order_and_default_login_behavior_remain_intact()
    {
        WpfTestHost.Invoke(() =>
        {
            var window = CreateWindow();
            try
            {
                var username = GetUsernameInput(window);
                var password = GetPasswordInput(window);
                var visiblePassword = GetVisiblePasswordInput(window);
                var signIn = Assert.IsType<Button>(window.FindName("SignInButton"));
                var toggle = GetVisibilityToggle(window);

                Assert.Equal(0, username.TabIndex);
                Assert.Equal(1, password.TabIndex);
                Assert.Equal(1, visiblePassword.TabIndex);
                Assert.Equal(2, signIn.TabIndex);
                Assert.True(signIn.IsDefault);
                Assert.Equal(3, toggle.TabIndex);
                Assert.IsNotType<Button>(toggle);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Window_close_clears_hidden_and_visible_password_values()
    {
        WpfTestHost.Invoke(() =>
        {
            var window = CreateWindow();
            GetPasswordInput(window).Password = "temporary";
            GetVisibilityToggle(window).IsChecked = true;
            GetVisiblePasswordInput(window).Text = "edited-temporary";

            InvokeOnClosing(window, new CancelEventArgs());

            AssertPasswordClearedAndHidden(window);
            window.Close();
        });
    }

    private static LoginWindow CreateWindow(
        IAuthenticationService? authenticationService = null,
        ICurrentUserSession? session = null) =>
        new(new LoginViewModel(
            authenticationService ?? new RecordingAuthenticationService(AuthenticationResult.Failed()),
            session ?? new EmptySession()));

    private static TextBox GetUsernameInput(LoginWindow window) =>
        Assert.IsType<TextBox>(window.FindName("UsernameInput"));

    private static LoginViewModel GetViewModel(LoginWindow window) =>
        Assert.IsType<LoginViewModel>(window.DataContext);

    private static PasswordBox GetPasswordInput(LoginWindow window) =>
        Assert.IsType<PasswordBox>(window.FindName("PasswordInput"));

    private static TextBox GetVisiblePasswordInput(LoginWindow window) =>
        Assert.IsType<TextBox>(window.FindName("VisiblePasswordInput"));

    private static ToggleButton GetVisibilityToggle(LoginWindow window) =>
        Assert.IsType<ToggleButton>(window.FindName("PasswordVisibilityToggle"));

    private static void AssertPasswordClearedAndHidden(LoginWindow window)
    {
        Assert.Equal(string.Empty, GetPasswordInput(window).Password);
        Assert.Equal(string.Empty, GetVisiblePasswordInput(window).Text);
        Assert.Equal(Visibility.Visible, GetPasswordInput(window).Visibility);
        Assert.Equal(Visibility.Collapsed, GetVisiblePasswordInput(window).Visibility);
        Assert.False(GetVisibilityToggle(window).IsChecked);
    }

    private static void InvokeOnClosing(LoginWindow window, CancelEventArgs args) =>
        typeof(LoginWindow)
            .GetMethod("OnClosing", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [args]);

    private sealed class RecordingAuthenticationService(AuthenticationResult result)
        : IAuthenticationService
    {
        public int CallCount { get; private set; }
        public string? LastPassword { get; private set; }

        public Task<AuthenticationResult> AuthenticateAsync(
            string username,
            string password,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastPassword = password;
            return Task.FromResult(result);
        }

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

    private sealed class AuthenticatedSession(AuthenticatedUser user) : ICurrentUserSession
    {
        public bool IsAuthenticated => true;
        public long? UserId => user.UserId;
        public string? FullName => user.FullName;
        public string? Username => user.Username;
        public UserRole? Role => user.Role;
    }
}
