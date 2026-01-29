using Random = System.Random;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BattleShip.Client
{
    public class GameLogic
    {
        public class Ship
        {
            public int Size { get; set; }
            public List<(int row, int col)> Cells { get; set; } = new List<(int row, int col)>();
            public bool IsPlaced { get; set; }
            public bool IsHorizontal { get; set; } = true;
            public (int row, int col)? StartPosition { get; set; }
            public int Hits { get; set; }
            public bool IsSunk => Hits >= Size && Size > 0;
        }

        private bool[,] _playerBoard;
        public List<Ship> PlayerShips { get; private set; }
        public bool AllShipsPlaced { get; private set; }
        private Random _random = new Random();

        private readonly List<int> _requiredShips = new List<int> { 4, 3, 3, 2, 2, 2, 1, 1, 1, 1 };
        private int _currentShipIndex = 0;
        private Ship _currentShipBeingPlaced = null;
        private bool _isPlacingShip = false;
        
        // Новое свойство: был ли выбран случайный соперник
        public bool IsRandomOpponentSelected { get; private set; } = false;

        public GameLogic()
        {
            _playerBoard = new bool[10, 10];
            PlayerShips = new List<Ship>();
            InitializeShips();
        }

        private void InitializeShips()
        {
            PlayerShips.Clear();
            foreach (var size in _requiredShips)
            {
                PlayerShips.Add(new Ship { Size = size });
            }
            _currentShipIndex = 0;
            _isPlacingShip = false;
            _currentShipBeingPlaced = null;
            AllShipsPlaced = false;
        }

        // Метод для выбора случайного соперника
        public void SelectRandomOpponent()
        {
            IsRandomOpponentSelected = true;
        }

        // Метод для отмены выбора соперника
        public void CancelOpponentSelection()
        {
            IsRandomOpponentSelected = false;
        }

        public Ship GetShipAt(int row, int col)
        {
            return PlayerShips.FirstOrDefault(ship =>
                ship.Cells.Any(c => c.row == row && c.col == col));
        }

        public void RegisterHit(int row, int col)
        {
            var ship = GetShipAt(row, col);
            if (ship != null && !ship.IsSunk)
            {
                ship.Hits++;
            }
        }

        public bool IsShipSunk(int row, int col)
        {
            var ship = GetShipAt(row, col);
            return ship?.IsSunk ?? false;
        }

        public List<(int row, int col)> GetShipCells(int row, int col)
        {
            var ship = GetShipAt(row, col);
            return ship?.Cells ?? new List<(int row, int col)>();
        }

        public Ship GetCurrentShip()
        {
            if (_currentShipIndex < PlayerShips.Count)
                return PlayerShips[_currentShipIndex];
            return null;
        }

        public bool TryPlaceShipCell(int row, int col)
        {
            // Если выбран случайный соперник, блокируем расстановку кораблей
            if (IsRandomOpponentSelected)
                return false;

            var currentShip = GetCurrentShip();
            if (currentShip == null || currentShip.IsPlaced) return false;

            // Для однопалубных кораблей (размер 1) размещаем сразу за один клик
            if (currentShip.Size == 1)
            {
                return PlaceSingleCellShip(row, col, currentShip);
            }

            // Для многопалубных кораблей используем пошаговую расстановку
            if (!_isPlacingShip || _currentShipBeingPlaced == null)
            {
                return StartNewShipPlacement(row, col, currentShip);
            }
            else
            {
                return ContinueShipPlacement(row, col, currentShip);
            }
        }

        private bool PlaceSingleCellShip(int row, int col, Ship currentShip)
        {
            // Проверяем, можно ли поставить клетку
            if (!CanPlaceCell(row, col))
                return false;

            // Ставим корабль сразу
            currentShip.Cells.Clear();
            currentShip.Cells.Add((row, col));
            currentShip.IsPlaced = true;
            currentShip.StartPosition = (row, col);
            _playerBoard[row, col] = true;
            
            _currentShipIndex++;
            CheckAllShipsPlaced();
            
            return true;
        }

        private bool StartNewShipPlacement(int row, int col, Ship currentShip)
        {
            // Проверяем, можно ли поставить первую клетку
            if (!CanPlaceCell(row, col))
                return false;

            // Проверяем, что корабль можно разместить хотя бы в одном направлении
            if (!CanShipFitAtPosition(row, col, currentShip.Size))
                return false;

            // Создаем временный корабль для расстановки
            _currentShipBeingPlaced = new Ship { Size = currentShip.Size };
            _currentShipBeingPlaced.StartPosition = (row, col);
            _currentShipBeingPlaced.Cells.Add((row, col));
            _currentShipBeingPlaced.IsHorizontal = true;

            _isPlacingShip = true;
            return true;
        }

        private bool ContinueShipPlacement(int row, int col, Ship currentShip)
        {
            if (!_isPlacingShip || _currentShipBeingPlaced == null)
                return false;

            // Проверяем, не пытаемся ли поставить ту же клетку
            if (_currentShipBeingPlaced.Cells.Any(c => c.row == row && c.col == col))
                return false;

            // Проверяем, можно ли поставить клетку
            if (!CanPlaceCell(row, col))
                return false;

            // Получаем начальную позицию
            var startPos = _currentShipBeingPlaced.StartPosition.Value;
            
            // Определяем направление по первой и второй клетке
            if (_currentShipBeingPlaced.Cells.Count == 1)
            {
                // Это вторая клетка - определяем направление
                if (row == startPos.row)
                {
                    _currentShipBeingPlaced.IsHorizontal = true;
                }
                else if (col == startPos.col)
                {
                    _currentShipBeingPlaced.IsHorizontal = false;
                }
                else
                {
                    return false; // Клетки не на одной линии
                }
            }

            // Проверяем, что клетка продолжает корабль в правильном направлении
            if (_currentShipBeingPlaced.IsHorizontal)
            {
                // Все клетки должны быть в одной строке
                if (row != startPos.row)
                    return false;

                // Определяем минимальную и максимальную колонку
                int minCol = Math.Min(col, _currentShipBeingPlaced.Cells.Min(c => c.col));
                int maxCol = Math.Max(col, _currentShipBeingPlaced.Cells.Max(c => c.col));
                
                // Проверяем, что разница между мин и макс соответствует размеру корабля
                if (maxCol - minCol + 1 > currentShip.Size)
                    return false;

                // Проверяем, что все клетки идут подряд без пропусков
                for (int c = minCol; c <= maxCol; c++)
                {
                    // Если есть пропуск в середине - не разрешаем
                    bool cellExists = _currentShipBeingPlaced.Cells.Any(cell => cell.col == c) || c == col;
                    if (!cellExists)
                        return false;
                }
            }
            else
            {
                // Все клетки должны быть в одном столбце
                if (col != startPos.col)
                    return false;

                // Определяем минимальную и максимальную строку
                int minRow = Math.Min(row, _currentShipBeingPlaced.Cells.Min(c => c.row));
                int maxRow = Math.Max(row, _currentShipBeingPlaced.Cells.Max(c => c.row));
                
                // Проверяем, что разница между мин и макс соответствует размеру корабля
                if (maxRow - minRow + 1 > currentShip.Size)
                    return false;

                // Проверяем, что все клетки идут подряд без пропусков
                for (int r = minRow; r <= maxRow; r++)
                {
                    // Если есть пропуск в середине - не разрешаем
                    bool cellExists = _currentShipBeingPlaced.Cells.Any(cell => cell.row == r) || r == row;
                    if (!cellExists)
                        return false;
                }
            }

            // Добавляем клетку к текущему кораблю
            _currentShipBeingPlaced.Cells.Add((row, col));

            // Если корабль завершен
            if (_currentShipBeingPlaced.Cells.Count == currentShip.Size)
            {
                return CompleteShipPlacement(currentShip);
            }

            return true;
        }

        private bool CompleteShipPlacement(Ship currentShip)
        {
            if (_currentShipBeingPlaced == null)
                return false;

            // Сортируем клетки для правильного порядка
            if (_currentShipBeingPlaced.IsHorizontal)
            {
                _currentShipBeingPlaced.Cells = _currentShipBeingPlaced.Cells
                    .OrderBy(c => c.col)
                    .ToList();
            }
            else
            {
                _currentShipBeingPlaced.Cells = _currentShipBeingPlaced.Cells
                    .OrderBy(c => c.row)
                    .ToList();
            }

            // Проверяем, что корабль не касается других кораблей
            foreach (var cell in _currentShipBeingPlaced.Cells)
            {
                if (!IsCellValidForShip(cell.row, cell.col, true))
                    return false;
            }

            // Переносим клетки в основной корабль
            currentShip.Cells.Clear();
            currentShip.Cells.AddRange(_currentShipBeingPlaced.Cells);
            currentShip.IsHorizontal = _currentShipBeingPlaced.IsHorizontal;
            currentShip.StartPosition = _currentShipBeingPlaced.StartPosition;
            currentShip.IsPlaced = true;

            // Отмечаем клетки на доске
            foreach (var cell in currentShip.Cells)
            {
                _playerBoard[cell.row, cell.col] = true;
            }

            // Сбрасываем состояние расстановки
            _currentShipBeingPlaced = null;
            _isPlacingShip = false;
            _currentShipIndex++;
            CheckAllShipsPlaced();

            return true;
        }

        private bool CanPlaceCell(int row, int col, bool skipSelfCheck = false)
        {
            if (row < 0 || row >= 10 || col < 0 || col >= 10)
                return false;
            
            if (!skipSelfCheck && _playerBoard[row, col])
                return false;
            
            return IsCellValidForShip(row, col, skipSelfCheck);
        }

        private bool IsCellValidForShip(int row, int col, bool skipSelfCheck = false)
        {
            // Проверяем соседние клетки (включая диагонали)
            for (int i = -1; i <= 1; i++)
            {
                for (int j = -1; j <= 1; j++)
                {
                    int checkRow = row + i;
                    int checkCol = col + j;
                    
                    if (checkRow >= 0 && checkRow < 10 && checkCol >= 0 && checkCol < 10)
                    {
                        if (skipSelfCheck && i == 0 && j == 0)
                            continue;
                            
                        if (_playerBoard[checkRow, checkCol])
                            return false;
                    }
                }
            }
            
            return true;
        }

        // Новый метод: проверка возможности размещения корабля из данной позиции
        private bool CanShipFitAtPosition(int row, int col, int shipSize)
        {
            // Проверяем горизонтальное размещение
            bool canFitHorizontally = false;
            if (col <= 10 - shipSize) // Проверяем, что корабль помещается по ширине
            {
                bool allCellsValid = true;
                for (int i = 0; i < shipSize; i++)
                {
                    if (!IsCellValidForShip(row, col + i))
                    {
                        allCellsValid = false;
                        break;
                    }
                }
                canFitHorizontally = allCellsValid;
            }

            // Проверяем вертикальное размещение
            bool canFitVertically = false;
            if (row <= 10 - shipSize) // Проверяем, что корабль помещается по высоте
            {
                bool allCellsValid = true;
                for (int i = 0; i < shipSize; i++)
                {
                    if (!IsCellValidForShip(row + i, col))
                    {
                        allCellsValid = false;
                        break;
                    }
                }
                canFitVertically = allCellsValid;
            }

            // Корабль можно разместить, если он помещается хотя бы в одном направлении
            return canFitHorizontally || canFitVertically;
        }

        public void RandomlyPlaceShips()
        {
            // Если выбран случайный соперник, блокируем случайную расстановку
            if (IsRandomOpponentSelected)
                return;

            ClearBoard();
            
            foreach (var ship in PlayerShips)
            {
                bool placed = false;
                int attempts = 0;
                
                while (!placed && attempts < 1000)
                {
                    attempts++;
                    
                    bool horizontal = _random.Next(0, 2) == 0;
                    int row, col;
                    
                    if (horizontal)
                    {
                        row = _random.Next(0, 10);
                        col = _random.Next(0, 11 - ship.Size);
                        
                        bool canPlace = true;
                        for (int i = 0; i < ship.Size; i++)
                        {
                            if (!IsCellValidForShip(row, col + i))
                            {
                                canPlace = false;
                                break;
                            }
                        }
                        
                        if (canPlace)
                        {
                            ship.Cells.Clear();
                            for (int i = 0; i < ship.Size; i++)
                            {
                                ship.Cells.Add((row, col + i));
                                _playerBoard[row, col + i] = true;
                            }
                            ship.IsHorizontal = true;
                            ship.StartPosition = (row, col);
                            ship.IsPlaced = true;
                            placed = true;
                        }
                    }
                    else
                    {
                        row = _random.Next(0, 11 - ship.Size);
                        col = _random.Next(0, 10);
                        
                        bool canPlace = true;
                        for (int i = 0; i < ship.Size; i++)
                        {
                            if (!IsCellValidForShip(row + i, col))
                            {
                                canPlace = false;
                                break;
                            }
                        }
                        
                        if (canPlace)
                        {
                            ship.Cells.Clear();
                            for (int i = 0; i < ship.Size; i++)
                            {
                                ship.Cells.Add((row + i, col));
                                _playerBoard[row + i, col] = true;
                            }
                            ship.IsHorizontal = false;
                            ship.StartPosition = (row, col);
                            ship.IsPlaced = true;
                            placed = true;
                        }
                    }
                }
                
                if (!placed)
                {
                    // Если не удалось разместить корабль, начинаем заново
                    ClearBoard();
                    RandomlyPlaceShips();
                    return;
                }
            }
            
            _currentShipIndex = PlayerShips.Count;
            CheckAllShipsPlaced();
        }

        public void ClearBoard()
        {
            // Если выбран случайный соперник, блокируем очистку поля
            if (IsRandomOpponentSelected)
                return;

            for (int i = 0; i < 10; i++)
                for (int j = 0; j < 10; j++)
                    _playerBoard[i, j] = false;
            
            foreach (var ship in PlayerShips)
            {
                ship.Cells.Clear();
                ship.IsPlaced = false;
                ship.StartPosition = null;
                ship.Hits = 0;
            }
            
            _currentShipIndex = 0;
            _currentShipBeingPlaced = null;
            _isPlacingShip = false;
            AllShipsPlaced = false;
        }

        public void RemoveLastCell()
        {
            // Если выбран случайный соперник, блокируем удаление клеток
            if (IsRandomOpponentSelected)
                return;

            if (_isPlacingShip && _currentShipBeingPlaced != null)
            {
                if (_currentShipBeingPlaced.Cells.Count > 0)
                {
                    _currentShipBeingPlaced.Cells.RemoveAt(_currentShipBeingPlaced.Cells.Count - 1);
                    
                    if (_currentShipBeingPlaced.Cells.Count == 0)
                    {
                        CancelCurrentShipPlacement();
                    }
                }
            }
            else if (_currentShipIndex > 0)
            {
                var lastShip = PlayerShips[_currentShipIndex - 1];
                if (lastShip.IsPlaced)
                {
                    foreach (var cell in lastShip.Cells)
                    {
                        _playerBoard[cell.row, cell.col] = false;
                    }
                    
                    lastShip.Cells.Clear();
                    lastShip.IsPlaced = false;
                    lastShip.StartPosition = null;
                    _currentShipIndex--;
                    AllShipsPlaced = false;
                }
            }
        }

        private void CheckAllShipsPlaced()
        {
            AllShipsPlaced = _currentShipIndex >= PlayerShips.Count && 
                           PlayerShips.All(s => s.IsPlaced);
        }

        public List<(int row, int col)> GetPlayerShipCells()
        {
            var cells = new List<(int row, int col)>();
            
            foreach (var ship in PlayerShips)
            {
                if (ship.IsPlaced)
                {
                    cells.AddRange(ship.Cells);
                }
            }
            
            return cells;
        }

        public List<(int row, int col)> GetCurrentShipBeingPlacedCells()
        {
            // Если выбран случайный соперник, не показываем клетки расставляемого корабля
            if (IsRandomOpponentSelected)
                return new List<(int row, int col)>();

            if (_currentShipBeingPlaced != null)
            {
                // Возвращаем отсортированные клетки
                if (_currentShipBeingPlaced.IsHorizontal)
                {
                    return _currentShipBeingPlaced.Cells
                        .OrderBy(c => c.col)
                        .ToList();
                }
                else
                {
                    return _currentShipBeingPlaced.Cells
                        .OrderBy(c => c.row)
                        .ToList();
                }
            }
            return new List<(int row, int col)>();
        }

        public string GetCurrentShipInfo()
        {
            // Если выбран случайный соперник, меняем сообщение
            if (IsRandomOpponentSelected)
                return "Ожидание соперника... Расстановка заблокирована";

            var currentShip = GetCurrentShip();
            
            if (_isPlacingShip && _currentShipBeingPlaced != null)
            {
                if (currentShip != null)
                {
                    return $"Корабль {currentShip.Size} клетки - поставлено {_currentShipBeingPlaced.Cells.Count}/{currentShip.Size} (ПКМ - отменить)";
                }
            }
            
            if (currentShip != null && !currentShip.IsPlaced)
            {
                if (currentShip.Size == 1)
                    return $"Кликните на клетку для однопалубного корабля (1 клетка)";
                else
                    return $"Нажмите на первую клетку для корабля размером {currentShip.Size} клетки";
            }
            
            return "Все корабли размещены";
        }

        public bool IsPlacingShip()
        {
            return _isPlacingShip;
        }

        public void CancelCurrentShipPlacement()
        {
            _currentShipBeingPlaced = null;
            _isPlacingShip = false;
        }

        // Обновленный метод для получения клеток предпросмотра с учетом ограничений
        public List<(int row, int col)> GetShipPreview(int row, int col)
        {
            // Если выбран случайный соперник, не показываем предпросмотр
            if (IsRandomOpponentSelected)
                return new List<(int row, int col)>();

            var preview = new List<(int row, int col)>();
            
            var currentShip = GetCurrentShip();
            if (currentShip == null || currentShip.IsPlaced || currentShip.Size == 1)
                return preview;

            if (!_isPlacingShip || _currentShipBeingPlaced == null)
            {
                // Предпросмотр первой клетки - только если корабль можно разместить
                if (CanPlaceCell(row, col) && CanShipFitAtPosition(row, col, currentShip.Size))
                {
                    preview.Add((row, col));
                }
            }
            else
            {
                // Предпросмотр продолжения корабля
                var startPos = _currentShipBeingPlaced.StartPosition.Value;
                int cellsPlaced = _currentShipBeingPlaced.Cells.Count;
                int cellsNeeded = currentShip.Size - cellsPlaced;

                if (cellsNeeded > 0)
                {
                    if (_currentShipBeingPlaced.Cells.Count == 1)
                    {
                        // Еще не определили направление - показываем оба варианта, но с учетом доступного места
                        if (row == startPos.row)
                        {
                            // Горизонтальное направление
                            int minCol = Math.Min(col, startPos.col);
                            int maxCol = Math.Max(col, startPos.col);
                            
                            // Проверяем, что мы не выходим за пределы допустимых размеров
                            if (maxCol - minCol + 1 <= currentShip.Size)
                            {
                                // Проверяем все клетки на валидность
                                bool allValid = true;
                                for (int c = minCol; c <= maxCol; c++)
                                {
                                    if (!CanPlaceCell(startPos.row, c))
                                    {
                                        allValid = false;
                                        break;
                                    }
                                }
                                
                                if (allValid)
                                {
                                    for (int c = minCol; c <= maxCol; c++)
                                    {
                                        if (c != startPos.col || row != startPos.row)
                                        {
                                            preview.Add((row, c));
                                        }
                                    }
                                }
                            }
                        }
                        
                        if (col == startPos.col)
                        {
                            // Вертикальное направление
                            int minRow = Math.Min(row, startPos.row);
                            int maxRow = Math.Max(row, startPos.row);
                            
                            // Проверяем, что мы не выходим за пределы допустимых размеров
                            if (maxRow - minRow + 1 <= currentShip.Size)
                            {
                                // Проверяем все клетки на валидность
                                bool allValid = true;
                                for (int r = minRow; r <= maxRow; r++)
                                {
                                    if (!CanPlaceCell(r, startPos.col))
                                    {
                                        allValid = false;
                                        break;
                                    }
                                }
                                
                                if (allValid)
                                {
                                    for (int r = minRow; r <= maxRow; r++)
                                    {
                                        if (r != startPos.row || col != startPos.col)
                                        {
                                            preview.Add((r, col));
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        // Направление уже определено
                        if (_currentShipBeingPlaced.IsHorizontal)
                        {
                            int minCol = _currentShipBeingPlaced.Cells.Min(c => c.col);
                            int maxCol = _currentShipBeingPlaced.Cells.Max(c => c.col);
                            
                            // Проверяем, что добавление новой клетки не превысит размер корабля
                            int potentialMin = Math.Min(col, minCol);
                            int potentialMax = Math.Max(col, maxCol);
                            
                            if (potentialMax - potentialMin + 1 <= currentShip.Size)
                            {
                                // Проверяем все клетки между minCol и col (или col и maxCol)
                                int checkStart = potentialMin;
                                int checkEnd = potentialMax;
                                
                                bool allValid = true;
                                for (int c = checkStart; c <= checkEnd; c++)
                                {
                                    if (!_currentShipBeingPlaced.Cells.Any(cell => cell.col == c) && !CanPlaceCell(startPos.row, c))
                                    {
                                        allValid = false;
                                        break;
                                    }
                                }
                                
                                if (allValid && CanPlaceCell(row, col))
                                {
                                    if (col < minCol)
                                    {
                                        preview.Add((row, col));
                                    }
                                    else if (col > maxCol)
                                    {
                                        preview.Add((row, col));
                                    }
                                }
                            }
                        }
                        else
                        {
                            int minRow = _currentShipBeingPlaced.Cells.Min(c => c.row);
                            int maxRow = _currentShipBeingPlaced.Cells.Max(c => c.row);
                            
                            // Проверяем, что добавление новой клетки не превысит размер корабля
                            int potentialMin = Math.Min(row, minRow);
                            int potentialMax = Math.Max(row, maxRow);
                            
                            if (potentialMax - potentialMin + 1 <= currentShip.Size)
                            {
                                // Проверяем все клетки между minRow и row (или row и maxRow)
                                int checkStart = potentialMin;
                                int checkEnd = potentialMax;
                                
                                bool allValid = true;
                                for (int r = checkStart; r <= checkEnd; r++)
                                {
                                    if (!_currentShipBeingPlaced.Cells.Any(cell => cell.row == r) && !CanPlaceCell(r, startPos.col))
                                    {
                                        allValid = false;
                                        break;
                                    }
                                }
                                
                                if (allValid && CanPlaceCell(row, col))
                                {
                                    if (row < minRow)
                                    {
                                        preview.Add((row, col));
                                    }
                                    else if (row > maxRow)
                                    {
                                        preview.Add((row, col));
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return preview;
        }

        // Новый метод для сброса состояния игры
        public void ResetGame()
        {
            CancelOpponentSelection();
            ClearBoard();
            InitializeShips();
        }
    }
}