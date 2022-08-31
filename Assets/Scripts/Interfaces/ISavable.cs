using UnityEngine;


public interface ISavable
{
	public int Type { get; internal set; }


	public Component Component => this as Component;


	public static void Save(SaveWriter saveFile, ISavable savable)
	{
		Debug.Assert(savable.Type >= 0 && savable.Type < GameController.Instance.m_savableFactory.m_savables.Length);

		saveFile.Write(savable.Type);
		Component savableComp = savable.Component;

		RoomController room = GameController.Instance.RoomFromPosition(savableComp.transform.position);
		if (room == null)
		{
			saveFile.Write(Vector3.zero); // TODO: better fallback?
		}
		else
		{
			saveFile.Write(savableComp.transform.position - room.transform.position);
		}

		saveFile.Write(savableComp.GetComponent<SpriteRenderer>().color);

		savable.SaveInternal(saveFile);
	}

	public static ISavable Load(SaveReader saveFile)
	{
		ISavable savable = GameController.Instance.m_savableFactory.Instantiate(saveFile.ReadInt32());
		Component savableComp = savable.Component;

		RoomController room = GameController.Instance.SpecialRooms[0];
		saveFile.Read(out Vector3 offsetFromRoom);
		savableComp.transform.position = room.BoundsInterior.ClosestPoint(room.transform.position + offsetFromRoom);

		savableComp.GetComponent<SpriteRenderer>().color = new(saveFile.ReadSingle(), saveFile.ReadSingle(), saveFile.ReadSingle(), saveFile.ReadSingle());

		savable.LoadInternal(saveFile);

		return savable;
	}



	protected void SaveInternal(SaveWriter saveFile);
	protected void LoadInternal(SaveReader saveFile);
}
