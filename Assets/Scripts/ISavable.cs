using UnityEngine;


public interface ISavable
{
	public int Type { get; internal set; }


	public Component Component => this as Component;


	public static void Save(SaveWriter saveFile, ISavable savable)
	{
		saveFile.Write(savable.Type);
		Component savableComp = savable.Component;

		// TODO: store position relative to containing room
		saveFile.Write(savableComp.transform.position);

		saveFile.Write(savableComp.GetComponent<SpriteRenderer>().color);

		savable.SaveInternal(saveFile);
	}

	public static ISavable Load(SaveReader saveFile)
	{
		ISavable savable = GameController.Instance.m_savableFactory.Instantiate(saveFile.ReadInt32());
		Component savableComp = savable.Component;

		// TODO: restore position relative to appropriate room
		savableComp.transform.position = new(saveFile.ReadSingle(), saveFile.ReadSingle(), saveFile.ReadSingle());

		savableComp.GetComponent<SpriteRenderer>().color = new Color(saveFile.ReadSingle(), saveFile.ReadSingle(), saveFile.ReadSingle(), saveFile.ReadSingle());

		savable.LoadInternal(saveFile);

		return savable;
	}



	protected void SaveInternal(SaveWriter saveFile);
	protected void LoadInternal(SaveReader saveFile);
}
