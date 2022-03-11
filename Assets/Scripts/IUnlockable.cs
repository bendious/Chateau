public interface IUnlockable
{
	public IUnlockable Parent { get; set; }


	public void SpawnKeys(RoomController lockRoom, RoomController[] keyRooms);

	public void Unlock(UnityEngine.GameObject key);
}
