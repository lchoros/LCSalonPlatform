using System.Windows.Input;

namespace Salon.Mobile.Core.Mvvm;

/// <summary>
/// A command that runs a task. A cancelled run ends quietly; any other exception is a bug and is left to crash, as it
/// would from an event handler. Tests call <see cref="ExecuteAsync"/> to await the run.
/// </summary>
public sealed class AsyncCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;

    public AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return _canExecute?.Invoke() ?? true;
    }

    public async void Execute(object? parameter)
    {
        await ExecuteAsync().ConfigureAwait(true);
    }

    public async Task ExecuteAsync()
    {
        try
        {
            await _execute().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Superseded or abandoned: nothing to show.
        }
    }

    public void NotifyCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>A task command with a parameter of a known type; any other parameter is ignored.</summary>
public sealed class AsyncCommand<T> : ICommand
{
    private readonly Func<T, Task> _execute;

    public AsyncCommand(Func<T, Task> execute)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = execute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter)
    {
        return parameter is T;
    }

    public async void Execute(object? parameter)
    {
        if (parameter is T value)
        {
            await ExecuteAsync(value).ConfigureAwait(true);
        }
    }

    public async Task ExecuteAsync(T parameter)
    {
        try
        {
            await _execute(parameter).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Superseded or abandoned: nothing to show.
        }
    }
}