using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Reflection;

public static partial class Cito
{
  public static SqlConnection GetConnection()
  {
    var configFile = Directory.GetCurrentDirectory() + "/app.config";
    var conn = new SqlConnection();
    var connStrings = ConfigurationManager.ConnectionStrings;

    if (File.Exists (configFile))
    {
      var map = new ExeConfigurationFileMap { ExeConfigFilename = configFile };
      var userLevel = ConfigurationUserLevel.None;
      var config = ConfigurationManager.OpenMappedExeConfiguration (map, userLevel);

      connStrings = config.ConnectionStrings.ConnectionStrings;

    }

    conn.ConnectionString = connStrings[connStrings.Count - 1].ConnectionString; 

    return conn;

  }

  public static IList<PropertyInfo> GetProperties(Type type)
  {
    return (from p in type.GetProperties()
                where
                  p.PropertyType == typeof(bool) ||
                  p.PropertyType == typeof(object) ||
                  p.PropertyType == typeof(string) ||
                  p.PropertyType == typeof(int) ||
                  p.PropertyType == typeof(decimal) ||
                  p.PropertyType == typeof(double) ||
                  p.PropertyType == typeof(Enum) ||
                  p.PropertyType == typeof(DateTime)
                select p).ToList();

  }

  public static void AddParams(ref SqlCommand cmd, object source)
  {
    var sql = cmd.CommandText.ToLower();
    var type = source.GetType();

    if (type == typeof(string) || type == typeof(int))
    {
      var start = sql.IndexOf("@");
      var str = sql.Substring(start + 1);
      // 95 underscore
      var finish = str.ToList().FindIndex(x => !char.IsLetter(x) && !char.IsNumber(x) && (int)x != 95);
      var newStr = str.Substring(0, finish).Trim();

      cmd.Parameters.AddWithValue($"@{newStr}", source);

    }

    foreach (var p in Cito.GetProperties(type))
    {
      Func<int, bool> has = (val) =>
      {
        return sql.Contains($"@{p.Name.ToLower()}{char.ConvertFromUtf32(val)}");
      };

      // 9 - tab, 10 - new line, 13 - return, 32 - space, 41 - close bracket, 44 - comma
      if (has(10) || has(10) || has(13) || has(32) || has(41) || has(44))
      {
        var value = p.GetValue(source);

        if (p.PropertyType == typeof(DateTime))
        {
          value = Convert.ToDateTime(value) == DateTime.MinValue ? DBNull.Value : value;
        }

        cmd.Parameters.AddWithValue($"@{p.Name}", value == null ? DBNull.Value : value);
      }

    }

  }

  public static void BindSelf(string sql, object source)
  {
    var data = Cito.GetDataTable(sql, source);

    foreach (DataRow row in data.Rows)
    {
      Cito.BindDataRow(row, source);
    }

  }

  public static void BindDataRow(DataRow row, object source)
  {
    foreach (var p in source.GetType().GetProperties())
    {
      var columns = (from x in row.Table.Columns.Cast<DataColumn>().ToList()
                     where x.Caption.ToLower() == p.Name.ToLower() && !DBNull.Value.Equals(row[x.Caption])
                     select x);
    
      foreach (DataColumn c in columns)
      {
        var value = row[c.Caption];

        if (p.PropertyType == typeof(bool))
        {
          value = (value.ToString() == 1.ToString()) || (value.ToString().ToLower() == true.ToString().ToLower());
        }

        if (object.ReferenceEquals(p.PropertyType.BaseType, typeof(Enum)))
        {
          value = Convert.ToInt32(value);
        }

        p.SetValue(source, value);

      }

    }

  }

  public static DataTable GetDataTable(string sql)
  {
    return Cito.GetDataTable(sql, null);
  }

  public static DataTable GetDataTable(string sql, object source)
  {
    using (var conn = Cito.GetConnection())
    {
      var cmd = new SqlCommand(sql, conn);

      if (source != null)
      {
        Cito.AddParams(ref cmd, source);
      }

      var data = new DataTable();

      using (var adapter = new SqlDataAdapter(cmd))
      {
        conn.Open();

        try
        {
          adapter.Fill(data);
        }
        catch(Exception ex) 
        {
          throw new Exception(ex.GetBaseException().Message);
        }

      }

      cmd.Dispose();

      return data;

    }

  }

  public static object GetScalar(string sql)
  {
    return Cito.GetScalar(sql, null);
  }

  public static object GetScalar(string sql, object source)
  {
    using (var conn = Cito.GetConnection())
    {
      var cmd = new SqlCommand(sql, conn);

      if (source != null)
      {
        Cito.AddParams(ref cmd, source);
      }

      conn.Open();

      var obj = cmd.ExecuteScalar();

      cmd.Dispose();

      return obj;

    }

  }

  public static void Execute(string sql)
  {
    Cito.Execute(sql, null);
  }

  public static void Execute(string sql, object source)
  {
    using (var conn = Cito.GetConnection())
    {
      var cmd = new SqlCommand(sql, conn);

      if (source != null)
      {
        Cito.AddParams(ref cmd, source);
      }

      cmd.CommandType = CommandType.Text;
      conn.Open();
      cmd.ExecuteNonQuery();
      cmd.Dispose();

    }

  }

}

public static partial class Cito<T>
{
  public static IList<T> Get(string sql)
  {
    return Cito<T>.Get(sql, null);
  }

  public static IList<T> Get(string sql, object source)
  {
    var list = new List<T>();

    foreach (DataRow row in Cito.GetDataTable(sql, source).Rows)
    {
      dynamic instance = Activator.CreateInstance(typeof(T));
      Cito.BindDataRow(row, instance);
      list.Add(instance);
    }

    return list;

  }

  public static T GetScalar(string sql)
  {
    return GetScalar(sql, null);
  }

  public static T GetScalar(string sql, object source)
  {
    using (var conn = Cito.GetConnection())
    {
      var cmd = new SqlCommand(sql, conn);

      if (source != null)
      {
        Cito.AddParams(ref cmd, source);
      }

      conn.Open();

      dynamic retVal = (T)cmd.ExecuteScalar();

      cmd.Dispose();

      return retVal;

    }

  }

}

public static partial class Cito<K, V>
{
  public static IDictionary<K, V> GetDictionary(string sql)
  {
    return GetDictionary(sql, null);;
  }

  public static IDictionary<K, V> GetDictionary(string sql, object source)
  {
    var dictionary = new Dictionary<K, V>();

    foreach (DataRow row in Cito.GetDataTable(sql, source).Rows)
    {
      dynamic k = (K)row[0];
      dynamic v = (V)row[1];

      dictionary.Add(k, v);

    }

    return dictionary;

  }

}