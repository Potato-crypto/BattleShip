using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BattleShip.Client
{
    public partial class ChatControl : UserControl
    {
        public event EventHandler<string> MessageSent;
        public event EventHandler Closed;
        public event EventHandler<int> UnreadCountChanged;
        
        private ObservableCollection<ChatMessageItem> _messages;
        private bool _isDragging = false;
        private Point _dragStart;
        private Point _chatStartPosition;
        private FrameworkElement _parentContainer;
        private int _unreadCount = 0;
        
        public int UnreadCount 
        { 
            get => _unreadCount;
            private set
            {
                _unreadCount = value;
                UnreadCountChanged?.Invoke(this, _unreadCount);
            }
        }
        
        public ChatControl()
        {
            InitializeComponent();
            _messages = new ObservableCollection<ChatMessageItem>();
            MessagesListView.ItemsSource = _messages;
            
            InitializeChat();
            Loaded += ChatControl_Loaded;
            Unloaded += ChatControl_Unloaded;
        }
        
        private void ChatControl_Loaded(object sender, RoutedEventArgs e)
        {
            // Находим родительский контейнер при загрузке
            _parentContainer = FindParentContainer();
            
            if (_parentContainer != null)
            {
                // Подписываемся на изменение размера родителя
                _parentContainer.SizeChanged += ParentContainer_SizeChanged;
            }
        }
        
        private void ChatControl_Unloaded(object sender, RoutedEventArgs e)
        {
            // Отписываемся от событий при выгрузке
            if (_parentContainer != null)
            {
                _parentContainer.SizeChanged -= ParentContainer_SizeChanged;
                _parentContainer = null;
            }
        }
        
        private FrameworkElement FindParentContainer()
        {
            // Ищем ближайший родительский контейнер (Grid)
            DependencyObject parent = VisualTreeHelper.GetParent(this);
            
            while (parent != null)
            {
                if (parent is Grid grid)
                {
                    return grid;
                }
                parent = VisualTreeHelper.GetParent(parent);
            }
            
            return Parent as FrameworkElement;
        }
        
        private void ParentContainer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // При изменении размера родителя ограничиваем положение чата
            ConstrainToParentBounds();
        }
        
        private void InitializeChat()
        {
            AddSystemMessage("Чат подключен. Вы можете общаться с соперником.");
            
            ChatTransform.X = 0;
            ChatTransform.Y = 0;
        }
        
        public void AddMessage(string sender, string message, bool isOwn = false)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;
        
            var chatMessage = new ChatMessageItem
            {
                Sender = sender,
                Text = message,
                Timestamp = DateTime.Now,
                IsSystem = false,
                IsOwn = isOwn,
                IsOpponent = !isOwn
            };
    
            _messages.Add(chatMessage);
            ScrollToBottom();
    
            // Увеличиваем счетчик непрочитанных, если чат не виден и это не наше сообщение
            if (Visibility != Visibility.Visible && !isOwn)
            {
                UnreadCount++;
            }
        }
        
        public void AddSystemMessage(string message)
        {
            var chatMessage = new ChatMessageItem
            {
                Sender = "Система",
                Text = message,
                Timestamp = DateTime.Now,
                IsSystem = true,
                IsOwn = false,
                IsOpponent = false
            };
            
            _messages.Add(chatMessage);
            ScrollToBottom();
        }
        
        public void ClearChat()
        {
            _messages.Clear();
        }
        
        private void ScrollToBottom()
        {
            if (_messages.Count > 0)
            {
                MessagesListView.ScrollIntoView(_messages[_messages.Count - 1]);
            }
        }
        
        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            SendMessage();
        }
        
        private void MessageInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SendMessage();
                e.Handled = true;
            }
        }
        
        private void SendMessage()
        {
            string message = MessageInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(message))
                return;
                
            MessageSent?.Invoke(this, message);
            MessageInput.Text = string.Empty;
            MessageInput.Focus();
        }
        
        private void CloseChatButton_Click(object sender, RoutedEventArgs e)
        {
            Closed?.Invoke(this, EventArgs.Empty);
        }
        
        public void SetOpponentName(string opponentName)
        {
            ChatTitle.Text = $"💬 Чат с {opponentName}";
        }
        
        public void MarkAsRead()
        {
            UnreadCount = 0;
        }
        
        // Логика перетаскивания
        private void ChatControl_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Проверяем, что клик был по заголовку чата или его границе
            var element = e.OriginalSource as FrameworkElement;
            if (element != null)
            {
                // Проверяем, является ли элемент частью заголовка чата
                bool isHeaderElement = IsInHeader(element);
                
                if (isHeaderElement)
                {
                    _isDragging = true;
                    _dragStart = e.GetPosition(this);
                    _chatStartPosition = new Point(ChatTransform.X, ChatTransform.Y);
                    this.CaptureMouse();
                    e.Handled = true;
                }
            }
        }
        
        private void ChatControl_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                this.ReleaseMouseCapture();
                e.Handled = true;
            }
        }
        
        private void ChatControl_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                Point currentPosition = e.GetPosition(this);
                
                // Вычисляем смещение
                double deltaX = currentPosition.X - _dragStart.X;
                double deltaY = currentPosition.Y - _dragStart.Y;
                
                // Обновляем положение чата
                double newX = _chatStartPosition.X + deltaX;
                double newY = _chatStartPosition.Y + deltaY;
                
                // Ограничиваем движение в пределах родительского контейнера
                ConstrainPosition(ref newX, ref newY);
                
                ChatTransform.X = newX;
                ChatTransform.Y = newY;
                
                e.Handled = true;
            }
        }
        
        private void ConstrainPosition(ref double x, ref double y)
        {
            if (_parentContainer == null)
                return;
            
            // Получаем размеры родительского контейнера
            double parentWidth = _parentContainer.ActualWidth;
            double parentHeight = _parentContainer.ActualHeight;
            
            // Размеры самого чата
            double chatWidth = this.ActualWidth;
            double chatHeight = this.ActualHeight;
            
            // Исходное положение чата (правый нижний угол с отступом 20px)
            double initialRight = parentWidth - chatWidth - 20;
            double initialBottom = parentHeight - chatHeight - 20;
            
            // Минимальные и максимальные допустимые координаты
            double minX = -initialRight - 20; // Можно сдвинуть влево до левого края с отступом 20px
            double maxX = 20; // Можно сдвинуть вправо до правого края с отступом 20px
            double minY = -initialBottom - 20; // Можно сдвинуть вверх до верхнего края с отступом 20px
            double maxY = 20; // Можно сдвинуть вниз до нижнего края с отступом 20px
            
            // Ограничиваем координаты
            x = Math.Max(minX, Math.Min(maxX, x));
            y = Math.Max(minY, Math.Min(maxY, y));
        }
        
        private void ConstrainToParentBounds()
        {
            double x = ChatTransform.X;
            double y = ChatTransform.Y;
            ConstrainPosition(ref x, ref y);
            ChatTransform.X = x;
            ChatTransform.Y = y;
        }
        
        private bool IsInHeader(FrameworkElement element)
        {
            // Проверяем, находится ли элемент в заголовке чата
            FrameworkElement current = element;
            while (current != null)
            {
                if (current.Name == "CloseChatButton" || 
                    current.Name == "ChatTitle" ||
                    (current is Border border && Grid.GetRow(border) == 0))
                {
                    return true;
                }
                current = VisualTreeHelper.GetParent(current) as FrameworkElement;
            }
            return false;
        }
        
        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            // При изменении размера ограничиваем положение чата
            ConstrainToParentBounds();
        }
    }
    
    public class ChatMessageItem
    {
        public string Sender { get; set; }
        public string Text { get; set; }
        public DateTime Timestamp { get; set; }
        public bool IsSystem { get; set; }
        public bool IsOwn { get; set; }
        public bool IsOpponent { get; set; }
    }
    public class ChatMessageTemplateSelector : DataTemplateSelector
    {
        public DataTemplate SystemTemplate { get; set; }
        public DataTemplate OwnTemplate { get; set; }
        public DataTemplate OpponentTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            if (item is ChatMessageItem chatMessage)
            {
                if (chatMessage.IsSystem)
                    return SystemTemplate;
                else if (chatMessage.IsOwn)
                    return OwnTemplate;
                else
                    return OpponentTemplate;
            }

            return base.SelectTemplate(item, container);
        }
    }
}