using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;


public class SaveWriter : System.IDisposable
{
#if DEBUG
	private readonly StreamWriter m_writer;
#else
	private readonly BinaryWriter m_writer;
#endif


	public SaveWriter()
	{
		m_writer = new(File.Create(SaveHelpers.SaveFilePath()));
		Write(SaveHelpers.m_saveVersion);
	}

	public void Dispose() => m_writer.Dispose();


	public void Write(int x) => m_writer.WriteLine(x);

	public void Write(float x) => m_writer.WriteLine(x);

	public void Write(Vector3 v)
	{
		Write(v.x);
		Write(v.y);
		Write(v.z);
	}

	public void Write(Color v)
	{
		Write(v.r);
		Write(v.g);
		Write(v.b);
		Write(v.a);
	}

	public void Write<T>(T[] array, System.Action<T> saveFunc)
	{
		Write(array.Length);
		foreach (T element in array)
		{
			saveFunc(element);
		}
	}
}


public class SaveReader : System.IDisposable
{
	public bool IsOpen => m_reader != null;


#if DEBUG
	private readonly StreamReader m_reader;
#else
	private readonly BinaryReader m_reader;
#endif


	public SaveReader()
	{
		string filePath = SaveHelpers.SaveFilePath();
		if (!File.Exists(filePath))
		{
			return;
		}

		m_reader = new(File.OpenRead(filePath));

		Read(out int saveVersion);
		if (saveVersion > SaveHelpers.m_saveVersion)
		{
			Debug.LogError("Unsupported save version: " + saveVersion);
			m_reader = null;
			return;
		}
	}

	public void Dispose() => m_reader?.Dispose();


	public int ReadInt32() => m_reader.ReadInt32();
	public void Read(out int x) => x = ReadInt32();

	public float ReadSingle() => m_reader.ReadSingle();
	public void Read(out float x) => x = ReadSingle();

	public void Read(out Vector3 v)
	{
		Read(out v.x);
		Read(out v.y);
		Read(out v.z);
	}

	public Color ReadColor() => new(ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle());
	public void Read(out Color v) => v = ReadColor();

	public T[] ReadArray<T>(System.Func<T> loadFunc)
	{
		T[] output = new T[ReadInt32()];
		for (int i = 0, n = output.Length; i < n; ++i)
		{
			output[i] = loadFunc();
		}
		return output;
	}
	public void Read<T>(out T[] array, System.Func<T> loadFunc) => array = ReadArray(loadFunc);
}


internal static class SaveHelpers
{
	internal const int m_saveVersion = 0;


#if DEBUG
	private const string m_saveFileSuffix = "-debug.dat";
#else
	private const string m_saveFileSuffix = ".dat";
#endif


	public static void Delete() => File.Delete(SaveFilePath());


	internal static string SaveFilePath() => Path.Combine(Application.persistentDataPath, "Save" + m_saveFileSuffix);


#if DEBUG
	internal static int ReadInt32(this StreamReader r)
	{
		return int.Parse(r.ReadLine());
	}

	internal static float ReadSingle(this StreamReader r)
	{
		return float.Parse(r.ReadLine());
	}

#else
	// TODO: better Write()/WriteLine() abstraction?
	internal static void WriteLine(this BinaryWriter w, int x)
	{
		w.Write(x);
	}

	internal static void WriteLine(this BinaryWriter w, float x)
	{
		w.Write(x);
	}
#endif
}
