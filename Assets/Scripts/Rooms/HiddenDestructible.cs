using UnityEngine;


[DisallowMultipleComponent, RequireComponent(typeof(Health))]
public class HiddenDestructible : MonoBehaviour
{
	[SerializeField] private GameObject m_hiddenPrefab;

	[SerializeField] private float m_hiddenPct = 0.5f;
	[SerializeField] private float m_lockedPct = 0.5f;
	[SerializeField] private float m_unlockedRarePct = 0.5f;


	private GameObject m_hiddenObject;


	private void Awake()
	{
		if (!GameController.Instance.m_allowHiddenDestructibles || Random.value > m_hiddenPct)
		{
			GetComponent<Health>().m_invincible = true;
			GetComponent<Collider2D>().enabled = false;
			enabled = false;
			return;
		}

		m_hiddenObject = Instantiate(m_hiddenPrefab, transform.position, transform.rotation, transform.parent);
		m_hiddenObject.SetActive(false);

		RoomController room = transform.parent.GetComponent<RoomController>();
		FurnitureController hiddenFurniture = m_hiddenObject.GetComponentInChildren<FurnitureController>(true);
		IUnlockable hiddenLock = m_hiddenObject.GetComponentInChildren<IUnlockable>(true);
		bool isLocked = hiddenLock != null && Random.value < m_lockedPct;

		if (hiddenFurniture != null)
		{
			hiddenFurniture.RandomizeSize(GetComponent<SpriteRenderer>().bounds.extents);
			hiddenFurniture.SpawnItems(isLocked || Random.value < m_unlockedRarePct, room.RoomType, 0, 0); // TODO: track/estimate room item count / remaining HiddenDestructibles?
		}
		if (hiddenLock != null)
		{
			if (isLocked)
			{
				RoomController[] keyRooms = new RoomController[] { room }; // TODO: spread out keys more?
				hiddenLock.SpawnKeysStatic(room, keyRooms);
				hiddenLock.SpawnKeysDynamic(room, keyRooms);
			}
			else
			{
				hiddenLock.Unlock(null);
			}
		}
	}

	private void OnDestroy()
	{
		if (GameController.IsSceneLoad || m_hiddenObject == null)
		{
			return;
		}
		m_hiddenObject.SetActive(true);
	}
}
