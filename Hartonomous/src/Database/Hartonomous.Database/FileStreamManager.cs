using System;
using System.Data.SqlTypes;
using Microsoft.SqlServer.Server;
using System.Data.SqlClient;

public static class FileStreamManager
{
    [SqlProcedure]
    public static void SaveComponentWeight(SqlGuid componentId, SqlBytes data)
    {
        if (componentId.IsNull || data.IsNull) return;

        using (SqlConnection conn = new SqlConnection("context connection=true"))
        {
            conn.Open();
            SqlTransaction tx = conn.BeginTransaction();
            try
            {
                string getPathQuery = "SELECT WeightData.PathName() FROM dbo.ComponentWeights WHERE ComponentId = @ComponentId";
                string txContextQuery = "SELECT GET_FILESTREAM_TRANSACTION_CONTEXT()";
                byte[] txContext = (byte[])new SqlCommand(txContextQuery, conn, tx).ExecuteScalar();
                string serverPath;

                using (var pathCmd = new SqlCommand(getPathQuery, conn, tx))
                {
                    pathCmd.Parameters.AddWithValue("@ComponentId", componentId.Value);
                    serverPath = (string)pathCmd.ExecuteScalar();
                }

                using (SqlFileStream sqlFileStream = new SqlFileStream(serverPath, txContext, System.IO.FileAccess.Write))
                {
                    sqlFileStream.Write(data.Value, 0, (int)data.Length);
                }

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }
    }
}