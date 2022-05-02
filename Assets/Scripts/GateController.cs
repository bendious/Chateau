using UnityEngine;


public class GateController : MonoBehaviour, IUnlockable
{
	public WeightedObject<GameObject>[] m_lockPrefabs;

	public WeightedObject<GameObject>[] m_ladderPrefabs;


	public GameObject Parent { get; set; }


	private IUnlockable m_child;


	private void OnTriggerEnter2D(Collider2D collider)
	{
		if (!IsValidNextKey(collider.gameObject))
		{
			return;
		}

		Parent.GetComponent<RoomController>().SpawnLadder(gameObject, m_ladderPrefabs?.RandomWeighted(), true);
		collider.GetComponent<IKey>().Use();
	}

	public void SpawnKeys(RoomController lockRoom, RoomController[] keyRooms)
	{
		GameObject lockPrefab = m_lockPrefabs.RandomWeighted();
		Vector3 spawnPos = lockRoom.InteriorPosition(0.0f, lockPrefab) + (Vector3)Utility.OriginToCenterY(lockPrefab, true);

		m_child = Instantiate(lockPrefab, spawnPos, Quaternion.identity, transform.parent).GetComponent<IUnlockable>();
		m_child.Parent = gameObject;
		m_child.SpawnKeys(lockRoom, keyRooms);
	}

	public bool IsValidNextKey(GameObject obj)
	{
		return m_child.IsValidNextKey(obj) /*|| m_ladderPrefabs.Exists(prefab => obj.SourcePrefab == prefab)*/; // TODO: allow any ladder to fit in any ladder gate?
	}

	public bool Unlock(IKey key)
	{
		GameController.Instance.AddCameraTargets(transform);
		Simulation.Schedule<ObjectDespawn>(1.0f).m_object = gameObject; // TODO: guarantee camera reaches us?
		return true;
	}
}
