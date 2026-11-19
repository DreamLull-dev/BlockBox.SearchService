using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace BlockBox.Controls
{
    public sealed class ScrollbarCursorTextBox : TextBox
    {
        private readonly InputSystemCursor _arrowCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
        private InputCursor? _previousCursor;
        private bool _usingArrowCursor;

        public ScrollbarCursorTextBox()
        {
            AddHandler(PointerMovedEvent, new PointerEventHandler(OnPointerMoved), true);
            AddHandler(PointerExitedEvent, new PointerEventHandler(OnPointerExited), true);
            AddHandler(PointerCanceledEvent, new PointerEventHandler(OnPointerExited), true);
        }

        private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            bool overHorizontalScrollBar = false;
            for (var element = e.OriginalSource as DependencyObject;
                 element != null && element != this;
                 element = VisualTreeHelper.GetParent(element))
            {
                if (element is ScrollBar scrollBar && scrollBar.Orientation == Orientation.Horizontal)
                {
                    overHorizontalScrollBar = true;
                    break;
                }
            }

            if (overHorizontalScrollBar && !_usingArrowCursor)
            {
                _previousCursor = ProtectedCursor;
                ProtectedCursor = _arrowCursor;
                _usingArrowCursor = true;
            }
            else if (!overHorizontalScrollBar)
            {
                RestoreCursor();
            }
        }

        private void OnPointerExited(object sender, PointerRoutedEventArgs e) => RestoreCursor();

        private void RestoreCursor()
        {
            if (!_usingArrowCursor) return;
            ProtectedCursor = _previousCursor;
            _previousCursor = null;
            _usingArrowCursor = false;
        }
    }
}
