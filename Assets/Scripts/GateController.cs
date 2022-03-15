using System.Linq;
using UnityEngine;


public class GateController : MonoBehaviour, IUnlockable
{
	public WeightedObject<GameObject>[] m_lockPrefabs;


	public IUnlockable Parent { get => null; set => UnityEngine.Assertions.Assert.IsTrue(false); }


	public void SpawnKeys(RoomController lockRoom, RoomController[] keyRooms)
	{
		GameObject lockPrefab = Utility.RandomWeighted(m_lockPrefabs);
		float height = -lockPrefab.GetComponentsInChildren<SpriteRenderer>().Min(renderer => renderer.bounds.min.y);
		Vector3 spawnPos = lockRoom.InteriorPosition(height, height) + Vector3.forward;
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
