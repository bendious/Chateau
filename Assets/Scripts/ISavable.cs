using System.IO;
using UnityEngine;


public interface ISavable
{
	public int Type { get; internal set; }


	public Component Component => this as Component;


	public static void Save(BinaryWriter saveFile, ISavable savable)
	{
		saveFile.Write(savable.Type);
		Component savableComp = savable.Component;

		// TODO: more generic write/read abstractions, store position relative to containing room
		saveFile.Write(savableComp.transform.position.x);
		saveFile.Write(savableComp.transform.position.y);
		saveFile.Write(savableComp.transform.position.z);

		SpriteRenderer renderer = savableComp.GetComponent<SpriteRenderer>();
		saveFile.Write(renderer.color.r);
		saveFile.Write(renderer.color.g);
		saveFile.Write(renderer.color.b);
		saveFile.Write(renderer.color.a);

		savable.SaveInternal(saveFile);
	}

	public static ISavable Load(BinaryReader saveFile)
	{
		ISavable savable = GameController.Instance.m_savableFactory.Instantiate(saveFile.ReadInt32());
		Component savableComp = savable.Component;

		// TODO: more generic write/read abstractions, restore position relative to appropriate room
		savableComp.transform.position = new(saveFile.ReadSingle(), saveFile.ReadSingle(), saveFile.ReadSingle());

		savableComp.GetComponent<SpriteRenderer>().color = new Color(saveFile.ReadSingle(), saveFile.ReadSingle(), saveFile.ReadSingle(), saveFile.ReadSingle());

		savable.LoadInternal(saveFile);

		return savable;
	}



	protected void SaveInternal(BinaryWriter saveFile);
	protected void LoadInternal(BinaryReader saveFile);
}
