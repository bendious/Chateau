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


	public SaveWriter(Scene scene)
	{
		m_writer = new(File.Create(SaveHelpers.SaveFilePath(scene)));
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
}


public class SaveReader : System.IDisposable
{
	public bool IsOpen => m_reader != null;


#if DEBUG
	private readonly StreamReader m_reader;
#else
	private readonly BinaryReader m_reader;
#endif


	public SaveReader(Scene scene)
	{
		string filePath = SaveHelpers.SaveFilePath(scene);
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

	public void Read(out Color v)
	{
		Read(out v.r);
		Read(out v.g);
		Read(out v.b);
		Read(out v.a);
	}
}


internal static class SaveHelpers
{
	internal const int m_saveVersion = 0;


#if DEBUG
	private const string m_saveFileSuffix = "-debug.dat";
#else
	private const string m_saveFileSuffix = ".dat";
#endif


	public static void Delete(Scene scene) => File.Delete(SaveFilePath(scene));


	internal static string SaveFilePath(Scene scene) => Path.Combine(Application.persistentDataPath, scene.name + m_saveFileSuffix);


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
