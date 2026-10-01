using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using IO_PJT.Services;
using IO_PJT.Utils;

namespace IO_PJT
{
    public class MainForm : Form
    {
        private TextBox txtDbPath = null!;
        private Button btnBrowse = null!;
        private Button btnCreateDb = null!;
        private TextBox txtTableName = null!;
        private Button btnCreateTable = null!;
        private Button btnInsertSample = null!;
        private RichTextBox txtLog = null!;
        private Label lblStatus = null!;
        private OpenFileDialog openFileDialog = null!;
        private Logger? _logger;

        public MainForm()
        {
            InitializeComponent();
            SetupDefaultPath();
        }

        private void InitializeComponent()
        {
            this.Text = "IO_PJT - Создание таблицы IoPoint в Access";
            this.Size = new Size(800, 700);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font = new Font("Segoe UI", 10);
            this.BackColor = Color.FromArgb(30, 30, 30);
            this.ForeColor = Color.White;

            // ----- Path Section -----
            var lblDbPath = new Label
            {
                Text = "📁 Путь к базе данных Access (.mdb или .accdb):",
                Location = new Point(20, 20),
                Size = new Size(400, 25),
                ForeColor = Color.White
            };

            txtDbPath = new TextBox
            {
                Location = new Point(20, 50),
                Size = new Size(560, 25),
                BackColor = Color.FromArgb(50, 50, 50),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            btnBrowse = new Button
            {
                Text = "📂 Обзор",
                Location = new Point(590, 48),
                Size = new Size(85, 30),
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnBrowse.Click += BtnBrowse_Click;

            btnCreateDb = new Button
            {
                Text = "🆕 Создать БД",
                Location = new Point(685, 48),
                Size = new Size(85, 30),
                BackColor = Color.FromArgb(60, 80, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnCreateDb.Click += BtnCreateDb_Click;

            // ----- Table Name Section -----
            var lblTableName = new Label
            {
                Text = "📝 Имя создаваемой таблицы:",
                Location = new Point(20, 95),
                Size = new Size(250, 25),
                ForeColor = Color.White
            };

            txtTableName = new TextBox
            {
                Text = "IoPoint",
                Location = new Point(20, 125),
                Size = new Size(300, 25),
                BackColor = Color.FromArgb(50, 50, 50),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            // ----- Buttons -----
            btnCreateTable = new Button
            {
                Text = "⚡ Создать таблицу",
                Location = new Point(20, 165),
                Size = new Size(180, 40),
                BackColor = Color.FromArgb(40, 120, 40),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 11, FontStyle.Bold)
            };
            btnCreateTable.Click += BtnCreateTable_Click;

            btnInsertSample = new Button
            {
                Text = "📥 Добавить тестовые данные",
                Location = new Point(210, 165),
                Size = new Size(200, 40),
                BackColor = Color.FromArgb(60, 80, 120),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10)
            };
            btnInsertSample.Click += BtnInsertSample_Click;

            // ----- Status Label -----
            lblStatus = new Label
            {
                Text = "Готов к работе",
                Location = new Point(20, 215),
                Size = new Size(600, 25),
                ForeColor = Color.LightGray
            };

            // ----- Log Section -----
            var lblLog = new Label
            {
                Text = "📋 Лог операций:",
                Location = new Point(20, 250),
                Size = new Size(150, 25),
                ForeColor = Color.White
            };

            txtLog = new RichTextBox
            {
                Location = new Point(20, 280),
                Size = new Size(750, 360),
                ReadOnly = true,
                BackColor = Color.Black,
                ForeColor = Color.LightGreen,
                Font = new Font("Consolas", 9),
                BorderStyle = BorderStyle.None
            };

            // ----- OpenFileDialog -----
            openFileDialog = new OpenFileDialog
            {
                Filter = "Access Database (*.mdb;*.accdb)|*.mdb;*.accdb|All files (*.*)|*.*",
                Title = "Выберите базу данных Access"
            };

            // Add controls
            this.Controls.Add(lblDbPath);
            this.Controls.Add(txtDbPath);
            this.Controls.Add(btnBrowse);
            this.Controls.Add(btnCreateDb);
            this.Controls.Add(lblTableName);
            this.Controls.Add(txtTableName);
            this.Controls.Add(btnCreateTable);
            this.Controls.Add(btnInsertSample);
            this.Controls.Add(lblStatus);
            this.Controls.Add(lblLog);
            this.Controls.Add(txtLog);
        }

        private void SetupDefaultPath()
        {
            string defaultDbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), 
                "IoPointDB.mdb");
            txtDbPath.Text = defaultDbPath;
        }

        private void BtnBrowse_Click(object sender, EventArgs e)
        {
            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                txtDbPath.Text = openFileDialog.FileName;
                GetLogger().Success($"Выбрана БД: {openFileDialog.FileName}");
            }
        }

        private void BtnCreateDb_Click(object sender, EventArgs e)
        {
            try
            {
                string dbPath = txtDbPath.Text.Trim();
                if (string.IsNullOrEmpty(dbPath))
                {
                    MessageBox.Show("Укажите путь для новой базы данных!", "Ошибка", 
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var dbService = new DatabaseService(dbPath);
                if (dbService.DatabaseExists())
                {
                    var result = MessageBox.Show("База данных уже существует. Пересоздать?", 
                        "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (result != DialogResult.Yes) return;

                    try
                    {
                        File.Delete(dbPath);
                        GetLogger().Info($"Удалена существующая БД: {dbPath}");
                    }
                    catch
                    {
                        MessageBox.Show("Не удалось удалить существующую БД. Возможно, она открыта в другой программе.", 
                            "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }

                dbService.CreateEmptyDatabase();
                GetLogger().Success($"Создана новая база данных: {dbPath}");
                UpdateStatus($"✅ БД создана: {Path.GetFileName(dbPath)}");

                MessageBox.Show($"База данных создана:\n{dbPath}", "Успех", 
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                GetLogger().Error($"Ошибка создания БД: {ex.Message}");
                UpdateStatus($"❌ Ошибка: {ex.Message}");
                MessageBox.Show($"Ошибка создания БД:\n{ex.Message}", "Ошибка", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnCreateTable_Click(object sender, EventArgs e)
        {
            try
            {
                string dbPath = txtDbPath.Text.Trim();
                string tableName = txtTableName.Text.Trim();

                if (string.IsNullOrEmpty(dbPath))
                {
                    MessageBox.Show("Укажите путь к базе данных!", "Ошибка", 
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (string.IsNullOrEmpty(tableName))
                {
                    MessageBox.Show("Введите имя таблицы!", "Ошибка", 
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var logger = GetLogger();
                var dbService = new DatabaseService(dbPath);

                // Проверяем существует ли БД
                if (!dbService.DatabaseExists())
                {
                    logger.Info("База данных не найдена. Создаем новую...");
                    dbService.CreateEmptyDatabase();
                    logger.Success("База данных создана");
                }

                logger.Info($"Создание таблицы '{tableName}'...");
                UpdateStatus($"⏳ Создание таблицы '{tableName}'...");

                // Создаем таблицу
                dbService.CreateTableIfNotExists(tableName);

                logger.Success($"Таблица '{tableName}' успешно создана!");
                UpdateStatus($"✅ Таблица '{tableName}' создана");

                // Показываем список таблиц
                var tables = dbService.GetTableNames();
                logger.Info($"Таблицы в БД: {string.Join(", ", tables)}");

                // Показываем структуру таблицы
                logger.Info($"Количество полей в таблице: {Models.TableStructure.GetFields().Count}");

                MessageBox.Show($"Таблица '{tableName}' успешно создана в БД:\n{dbPath}", 
                    "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                GetLogger().Error($"Ошибка создания таблицы: {ex.Message}");
                UpdateStatus($"❌ Ошибка: {ex.Message}");
                MessageBox.Show($"Ошибка создания таблицы:\n{ex.Message}", 
                    "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnInsertSample_Click(object sender, EventArgs e)
        {
            try
            {
                string dbPath = txtDbPath.Text.Trim();
                string tableName = txtTableName.Text.Trim();

                if (string.IsNullOrEmpty(dbPath))
                {
                    MessageBox.Show("Укажите путь к базе данных!", "Ошибка", 
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (string.IsNullOrEmpty(tableName))
                {
                    MessageBox.Show("Введите имя таблицы!", "Ошибка", 
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var logger = GetLogger();
                var dbService = new DatabaseService(dbPath);

                if (!dbService.DatabaseExists())
                {
                    logger.Error("База данных не найдена!");
                    MessageBox.Show("База данных не найдена. Сначала создайте базу данных и таблицу.", 
                        "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (!dbService.TableExists(tableName))
                {
                    logger.Error($"Таблица '{tableName}' не существует!");
                    MessageBox.Show($"Таблица '{tableName}' не существует. Сначала создайте таблицу.", 
                        "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                logger.Info("Добавление тестовых данных...");
                dbService.InsertSampleData(tableName);
                logger.Success("Тестовые данные добавлены!");

                MessageBox.Show("Тестовые данные успешно добавлены в таблицу!", 
                    "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                GetLogger().Error($"Ошибка добавления данных: {ex.Message}");
                MessageBox.Show($"Ошибка добавления данных:\n{ex.Message}", 
                    "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private Logger GetLogger()
        {
            if (_logger == null)
                _logger = new Logger(txtLog);
            return _logger;
        }

        private void UpdateStatus(string message)
        {
            if (lblStatus.InvokeRequired)
            {
                lblStatus.Invoke(new Action(() => lblStatus.Text = message));
            }
            else
            {
                lblStatus.Text = message;
            }
        }
    }
}