using System;
using System.Collections.Generic;

namespace Pivot.Core.Commands
{
    /// <summary>
    /// Undo and redo. A command that reports no change is not recorded, so pressing
    /// undo after a refused drop takes back the last thing that actually happened
    /// rather than doing nothing.
    /// </summary>
    public sealed class CommandHistory
    {
        readonly List<ICommand> _done;
        readonly List<ICommand> _undone;

        public CommandHistory(int capacity = 64)
        {
            Capacity = capacity;
            _done = new List<ICommand>(capacity);
            _undone = new List<ICommand>(capacity);
        }

        /// <summary>Oldest entries are dropped once the history is this long.</summary>
        public int Capacity { get; set; }

        public event Action Changed;

        public bool CanUndo
        {
            get { return _done.Count > 0; }
        }

        public bool CanRedo
        {
            get { return _undone.Count > 0; }
        }

        public string NextUndoName
        {
            get { return _done.Count > 0 ? _done[_done.Count - 1].Name : null; }
        }

        public string NextRedoName
        {
            get { return _undone.Count > 0 ? _undone[_undone.Count - 1].Name : null; }
        }

        public int DoneCount
        {
            get { return _done.Count; }
        }

        public bool Run(ICommand command, List<TreeStep> steps)
        {
            if (command == null) throw new ArgumentNullException("command");

            if (!command.Execute(steps)) return false;

            _done.Add(command);
            _undone.Clear();

            while (_done.Count > Capacity) _done.RemoveAt(0);

            RaiseChanged();
            return true;
        }

        public bool Undo()
        {
            if (_done.Count == 0) return false;

            int last = _done.Count - 1;
            ICommand command = _done[last];
            _done.RemoveAt(last);
            command.Undo();
            _undone.Add(command);

            RaiseChanged();
            return true;
        }

        public bool Redo()
        {
            if (_undone.Count == 0) return false;

            int last = _undone.Count - 1;
            ICommand command = _undone[last];
            _undone.RemoveAt(last);
            command.Redo();
            _done.Add(command);

            RaiseChanged();
            return true;
        }

        public void Clear()
        {
            _done.Clear();
            _undone.Clear();
            RaiseChanged();
        }

        void RaiseChanged()
        {
            Action handler = Changed;
            if (handler != null) handler();
        }
    }
}
