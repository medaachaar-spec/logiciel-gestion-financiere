using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace FinancialManagement.Desktop.Services;

public sealed class BackupDialogService : IBackupDialogService
{
    public string? SelectExternalDirectory()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choisir l'emplacement de la sauvegarde externe",
            Multiselect = false
        };
        return ShowDialog(dialog) ? dialog.FolderName : null;
    }

    public string? SelectBackupFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choisir une sauvegarde à restaurer",
            Filter = "Sauvegardes Tresoria (*.fmbak)|*.fmbak",
            CheckFileExists = true,
            Multiselect = false
        };
        return ShowDialog(dialog) ? dialog.FileName : null;
    }

    public bool ConfirmRestore(string backupFilePath)
    {
        var fileName = Path.GetFileName(backupFilePath);
        return MessageBox.Show(
            "Cette opération remplacera les données actuelles par la sauvegarde sélectionnée :"
                + Environment.NewLine + fileName
                + Environment.NewLine + Environment.NewLine
                + "Une sauvegarde de sécurité des données actuelles sera créée avant le remplacement. Continuer ?",
            "Confirmer la restauration",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    public void ShowRestoreSucceeded() =>
        MessageBox.Show(
            "La restauration est terminée. Vous allez revenir à l'écran de connexion.",
            "Restauration terminée",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

    private static bool ShowDialog(CommonDialog dialog)
    {
        var owner = System.Windows.Application.Current?.MainWindow;
        return owner is null ? dialog.ShowDialog() == true : dialog.ShowDialog(owner) == true;
    }
}
