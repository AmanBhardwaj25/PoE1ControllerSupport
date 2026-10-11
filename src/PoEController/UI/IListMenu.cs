using UnityEngine;

namespace PoEController.UI
{
    /// <summary>A small popup list drawn by <see cref="ItemMenuView"/> (inventory item menu, inspect actions, compare choices).</summary>
    internal interface IListMenu
    {
        bool MenuOpen { get; }
        string Title { get; }
        /// <summary>Screen rect (GUI coordinates) of what the list belongs to; it sits beside it.</summary>
        Rect Anchor { get; }
        int Focus { get; }
        int Count { get; }
        string LabelAt(int i);
        /// <summary>Null when usable; otherwise a short reason drawn dimmed next to the label.</summary>
        string DisabledAt(int i);
    }
}
