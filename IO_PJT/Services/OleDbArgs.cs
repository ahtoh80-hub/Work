using System;
using System.Data.OleDb;

namespace IO_PJT.Services
{
    /// <summary>
    /// Добавление параметров OleDb с корректными типами для ACE/Jet.
    /// Важно: DateTime НЕЛЬЗЯ передавать через AddWithValue (падает
    /// «Data type mismatch in criteria expression») — только OleDbType.Date.
    /// long (ID-счётчика) приводится к int: Access LONG не имеет BIGINT.
    /// </summary>
    public static class OleDbArgs
    {
        public static void Add(OleDbCommand cmd, object? value)
        {
            switch (value)
            {
                case DateTime dt:
                    var pDate = cmd.Parameters.Add("?", OleDbType.Date);
                    pDate.Value = dt;
                    break;
                case long l:
                    cmd.Parameters.AddWithValue("?", checked((int)l));
                    break;
                default:
                    cmd.Parameters.AddWithValue("?", value ?? DBNull.Value);
                    break;
            }
        }
    }
}
