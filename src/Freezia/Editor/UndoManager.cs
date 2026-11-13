using System;
using System.Collections.Generic;

namespace Freezia.Editor;

/// <summary>Unlimited undo/redo of (undo, redo) action pairs. A new action clears the redo stack.</summary>
public sealed class UndoManager
{
    private sealed record Command(string Name, Action Undo, Action Redo);

    private readonly Stack<Command> _undo = new();
    private readonly Stack<Command> _redo = new();

    public event EventHandler? Changed;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Record an action that has ALREADY been applied.</summary>
    public void Push(string name, Action undo, Action redo)
    {
        _undo.Push(new Command(name, undo, redo));
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Apply <paramref name="redo"/> now and record it.</summary>
    public void Do(string name, Action undo, Action redo)
    {
        redo();
        Push(name, undo, redo);
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        var c = _undo.Pop();
        c.Undo();
        _redo.Push(c);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        var c = _redo.Pop();
        c.Redo();
        _undo.Push(c);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
