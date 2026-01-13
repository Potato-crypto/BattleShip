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
        }

        private bool[,] _playerBoard;
        public System.Collections.Generic.List<Ship> PlayerShips { get; private set; }
        public bool AllShipsPlaced { get; private set; }
        private Random _random = new Random();

        private readonly System.Collections.Generic.List<int> _requiredShips = new System.Collections.Generic.List<int> { 4, 3, 3, 2, 2, 2, 1, 1, 1, 1 };
        private int _currentShipIndex = 0;
        private Ship _currentShipBeingPlaced = null;
        private bool _isPlacingShip = false;

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
        }

        public Ship GetCurrentShip()
        {
            if (_currentShipIndex < PlayerShips.Count)
                return PlayerShips[_currentShipIndex];
            return null;
        }

        public bool TryPlaceShipCell(int row, int col)
        {
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
            currentShip.Cells.Add((row, col));
            currentShip.IsPlaced = true;
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

                // Клетки должны быть последовательными
                var cols = _currentShipBeingPlaced.Cells.Select(c => c.col).ToList();
                cols.Add(col);
                cols.Sort();

                // Проверяем, что все клетки идут подряд
                for (int i = 1; i < cols.Count; i++)
                {
                    if (cols[i] != cols[i - 1] + 1)
                        return false;
                }
            }
            else
            {
                // Все клетки должны быть в одном столбце
                if (col != startPos.col)
                    return false;

                // Клетки должны быть последовательными
                var rows = _currentShipBeingPlaced.Cells.Select(c => c.row).ToList();
                rows.Add(row);
                rows.Sort();

                // Проверяем, что все клетки идут подряд
                for (int i = 1; i < rows.Count; i++)
                {
                    if (rows[i] != rows[i - 1] + 1)
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

            // Проверяем, что корабль не касается других кораблей
            foreach (var cell in _currentShipBeingPlaced.Cells)
            {
                if (!CanPlaceCell(cell.row, cell.col, true))
                    return false;
            }

            // Переносим клетки в основной корабль
            currentShip.Cells.Clear();
            currentShip.Cells.AddRange(_currentShipBeingPlaced.Cells);
            currentShip.IsHorizontal = _currentShipBeingPlaced.IsHorizontal;
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

        public void RandomlyPlaceShips()
        {
            ClearBoard();
            
            foreach (var ship in PlayerShips)
            {
                bool placed = false;
                int attempts = 0;
                
                while (!placed && attempts < 100)
                {
                    attempts++;
                    
                    bool horizontal = _random.Next(0, 2) == 0;
                    
                    if (horizontal)
                    {
                        int row = _random.Next(0, 10);
                        int col = _random.Next(0, 11 - ship.Size);
                        
                        bool canPlace = true;
                        for (int i = 0; i < ship.Size; i++)
                        {
                            if (!CanPlaceCell(row, col + i))
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
                            ship.IsPlaced = true;
                            placed = true;
                        }
                    }
                    else
                    {
                        int row = _random.Next(0, 11 - ship.Size);
                        int col = _random.Next(0, 10);
                        
                        bool canPlace = true;
                        for (int i = 0; i < ship.Size; i++)
                        {
                            if (!CanPlaceCell(row + i, col))
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
                            ship.IsPlaced = true;
                            placed = true;
                        }
                    }
                }
                
                if (!placed)
                {
                    RandomlyPlaceShips();
                    return;
                }
            }
            
            _currentShipIndex = PlayerShips.Count;
            CheckAllShipsPlaced();
        }

        public void ClearBoard()
        {
            for (int i = 0; i < 10; i++)
                for (int j = 0; j < 10; j++)
                    _playerBoard[i, j] = false;
            
            foreach (var ship in PlayerShips)
            {
                ship.Cells.Clear();
                ship.IsPlaced = false;
            }
            
            _currentShipIndex = 0;
            _currentShipBeingPlaced = null;
            _isPlacingShip = false;
            AllShipsPlaced = false;
        }

        public void RemoveLastCell()
        {
            if (_isPlacingShip && _currentShipBeingPlaced != null)
            {
                if (_currentShipBeingPlaced.Cells.Count > 0)
                {
                    _currentShipBeingPlaced.Cells.RemoveAt(_currentShipBeingPlaced.Cells.Count - 1);
                    
                    if (_currentShipBeingPlaced.Cells.Count == 0)
                    {
                        _currentShipBeingPlaced = null;
                        _isPlacingShip = false;
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
            if (_currentShipBeingPlaced != null)
            {
                return _currentShipBeingPlaced.Cells;
            }
            return new List<(int row, int col)>();
        }

        public string GetCurrentShipInfo()
        {
            if (_isPlacingShip && _currentShipBeingPlaced != null)
            {
                var currentShip = GetCurrentShip();
                if (currentShip != null)
                {
                    return $"Корабль {currentShip.Size} клетки - поставлено {_currentShipBeingPlaced.Cells.Count}/{currentShip.Size} (ПКМ - отменить)";
                }
            }
            
            var ship = GetCurrentShip();
            if (ship != null && !ship.IsPlaced)
            {
                if (ship.Size == 1)
                    return $"Кликните на клетку для однопалубного корабля (1 клетка)";
                else
                    return $"Нажмите на первую клетку для корабля размером {ship.Size} клетки";
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
    }
}
