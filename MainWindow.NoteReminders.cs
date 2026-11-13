using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using YellowBlackEditor.Models;
using YellowBlackEditor.ViewModels;
using YellowBlackEditor.Views;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Orientation = System.Windows.Controls.Orientation;

namespace YellowBlackEditor
{
    public partial class MainWindow
    {
        private Popup? _noteRemindersPopup;

        private List<Reminder> NoteReminders(NodeViewModel note)
        {
            var rel = RelativePath(note);
            return _generalReminders
                .Where(r => !r.Done && string.Equals(r.NotePath, rel, StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r.Due ?? DateTime.MaxValue)
                .ToList();
        }

        private void AddNoteReminder(NodeViewModel note, FrameworkElement anchor)
        {
            ReminderPicker.Open(anchor, null, note.Name, true, false,
                (due, text) =>
                {
                    _generalReminders.Add(new Reminder
                    {
                        NotePath = RelativePath(note),
                        Text = string.IsNullOrWhiteSpace(text) ? note.Name : text,
                        Due = due
                    });
                    SaveGeneralReminders();
                    RefreshReminders();
                    Status("Напомню " + DateText.Human(due));
                });
        }

        private void EditNoteReminder(NodeViewModel note, Reminder r, FrameworkElement anchor)
        {
            ReminderPicker.Open(anchor, r.Due, r.Text, true, true,
                (due, text) =>
                {
                    r.Text = string.IsNullOrWhiteSpace(text) ? note.Name : text;
                    r.Due = due;
                    r.Notified = false;
                    SaveGeneralReminders();
                    RefreshReminders();
                    Status("Напомню " + DateText.Human(due));
                },
                () =>
                {
                    _generalReminders.Remove(r);
                    SaveGeneralReminders();
                    RefreshReminders();
                    Status("Напоминание убрано");
                });
        }

        private void NoteReminder_Click(object sender, RoutedEventArgs e)
        {
            var note = _currentDocNode;
            if (note == null) return;
            if (NoteReminders(note).Count == 0)
            {
                AddNoteReminder(note, NoteReminderBtn);
                return;
            }
            ShowNoteRemindersPopup(note);
        }

        private void ShowNoteRemindersPopup(NodeViewModel note)
        {
            if (_noteRemindersPopup != null) _noteRemindersPopup.IsOpen = false;
            var list = NoteReminders(note);
            var now = DateTime.Now;

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = "Напоминания по заметке",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(4, 0, 0, 10)
            });

            foreach (var r in list)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var check = new CheckBox
                {
                    Style = (Style)FindResource("DoneCheck"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0, 8, 0),
                    ToolTip = "Сделано"
                };
                var reminder = r;
                check.Checked += (_, _) =>
                {
                    reminder.Done = true;
                    SaveGeneralReminders();
                    RefreshReminders();
                    Status("Напоминание отмечено сделанным");
                    if (NoteReminders(note).Count > 0) ShowNoteRemindersPopup(note);
                    else if (_noteRemindersPopup != null) _noteRemindersPopup.IsOpen = false;
                };
                row.Children.Add(check);

                var overdue = r.Due != null && r.Due <= now;
                var info = new StackPanel();
                info.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(r.Text) ? note.Name : r.Text, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis });
                var when = new TextBlock
                {
                    Text = r.Due == null ? "без срока" : (overdue ? "просрочено: " : "") + DateText.Human(r.Due.Value),
                    FontSize = 12
                };
                when.SetResourceReference(TextBlock.ForegroundProperty, overdue ? "DangerBrush" : "SubTextBrush");
                info.Children.Add(when);
                var edit = new Button
                {
                    Style = (Style)FindResource("TextButton"),
                    Content = info,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                    Padding = new Thickness(6, 4, 6, 4),
                    Height = double.NaN,
                    ToolTip = "Изменить время или текст"
                };
                edit.Click += (_, _) =>
                {
                    if (_noteRemindersPopup != null) _noteRemindersPopup.IsOpen = false;
                    EditNoteReminder(note, reminder, NoteReminderBtn);
                };
                Grid.SetColumn(edit, 1);
                row.Children.Add(edit);

                var del = new Button
                {
                    Style = (Style)FindResource("DangerIconButton"),
                    Content = "",
                    ToolTip = "Удалить напоминание",
                    VerticalAlignment = VerticalAlignment.Center
                };
                del.Click += (_, _) =>
                {
                    _generalReminders.Remove(reminder);
                    SaveGeneralReminders();
                    RefreshReminders();
                    Status("Напоминание убрано");
                    if (NoteReminders(note).Count > 0) ShowNoteRemindersPopup(note);
                    else if (_noteRemindersPopup != null) _noteRemindersPopup.IsOpen = false;
                };
                Grid.SetColumn(del, 2);
                row.Children.Add(del);
                stack.Children.Add(row);
            }

            var add = new Button
            {
                Style = (Style)FindResource("TextButton"),
                Content = "+ Новое напоминание",
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                Margin = new Thickness(0, 6, 0, 0)
            };
            add.Click += (_, _) =>
            {
                if (_noteRemindersPopup != null) _noteRemindersPopup.IsOpen = false;
                AddNoteReminder(note, NoteReminderBtn);
            };
            stack.Children.Add(add);

            var border = new Border
            {
                Child = stack,
                Width = 420,
                Padding = new Thickness(14, 12, 14, 12),
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1)
            };
            border.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
            border.SetResourceReference(Border.BorderBrushProperty, "HoverBorderBrush");
            border.SetResourceReference(TextElement.ForegroundProperty, "TextBrush");

            _noteRemindersPopup = new Popup
            {
                Child = border,
                PlacementTarget = NoteReminderBtn,
                Placement = PlacementMode.Bottom,
                VerticalOffset = 6,
                StaysOpen = false,
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.Fade
            };
            _noteRemindersPopup.KeyDown += (_, k) =>
            {
                if (k.Key == Key.Escape && _noteRemindersPopup != null) _noteRemindersPopup.IsOpen = false;
            };
            _noteRemindersPopup.IsOpen = true;
        }
    }
}
