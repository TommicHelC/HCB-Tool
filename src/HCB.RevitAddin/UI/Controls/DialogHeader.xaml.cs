using System.Windows;
using System.Windows.Controls;

namespace HCB.RevitAddin.UI.Controls
{
    /// <summary>
    /// Standard window header: title, optional one-line description and a divider.
    /// </summary>
    public partial class DialogHeader : UserControl
    {
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(
                nameof(Title),
                typeof(string),
                typeof(DialogHeader),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty DescriptionProperty =
            DependencyProperty.Register(
                nameof(Description),
                typeof(string),
                typeof(DialogHeader),
                new PropertyMetadata(string.Empty, OnDescriptionChanged));

        private static readonly DependencyPropertyKey HasDescriptionPropertyKey =
            DependencyProperty.RegisterReadOnly(
                nameof(HasDescription),
                typeof(bool),
                typeof(DialogHeader),
                new PropertyMetadata(false));

        public static readonly DependencyProperty HasDescriptionProperty = HasDescriptionPropertyKey.DependencyProperty;

        public DialogHeader()
        {
            InitializeComponent();
        }

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public string Description
        {
            get => (string)GetValue(DescriptionProperty);
            set => SetValue(DescriptionProperty, value);
        }

        public bool HasDescription => (bool)GetValue(HasDescriptionProperty);

        private static void OnDescriptionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            d.SetValue(HasDescriptionPropertyKey, !string.IsNullOrWhiteSpace(e.NewValue as string));
        }
    }
}
