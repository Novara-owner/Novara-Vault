/* ========== RelayCommand - MVVM Command ==========
Function: ICommand implementation for tray menu Command bindings
Corresponding UI: RelayCommand.cs
Logic Range: Whole file business logic of this module
*/
namespace Novara.Services;

public sealed class RelayCommand : System.Windows.Input.ICommand
{
    private readonly Action _execute;

    public RelayCommand(Action execute) => _execute = execute;

    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => _execute();
}
