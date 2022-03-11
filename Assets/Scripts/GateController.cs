using System.Linq;
using UnityEngine;


public class GateController : MonoBehaviour, IUnlockable
{
	public WeightedObject<GameObject>[] m_lockPrefabs;


	public IUnlockable Parent { get => null; set => UnityEngine.Assertions.Assert.IsTrue(false); }


	public void SpawnKeys(RoomController lockRoom, RoomController[] keyRooms)
	{
		GameObject lockPrefab = Utility.RandomWeighted(m_lockPrefabs);
		Vector3 spawnPos = lockRoom.InteriorPosition(true) + new Vector3(0.0f, -lockPrefab.GetComponentsInChildren<SpriteRenderer>().Min(renderer => renderer.bounds.min.y), 1.0f);
		IUnlockable unlockable = Instantiate(lockPrefab, spawnPos, Quaternion.identity).GetComponent<IUnlockable>();
		unlockable.Parent = this;

		unlockable.SpawnKeys(lockRoom, keyRooms);
	}

	public void Unlock(GameObject key)
	{
		GameController.Instance.AddCameraTargets(transform);
		Simulation.Schedule<ObjectDespawn>(1.0f).m_object = gameObject; // TODO: guarantee camera reaches us?
	}
}
