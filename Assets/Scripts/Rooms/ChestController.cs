using UnityEngine;


[DisallowMultipleComponent, RequireComponent(typeof(SpriteRenderer), typeof(Collider2D), typeof(AudioSource))]
public class ChestController : FurnitureController, IInteractable, IUnlockable
{
	[SerializeField] private WeightedObject<GameObject>[] m_keyPrefabs;
	[SerializeField] private WeightedObject<AudioClip>[] m_unlockSFX;
	[SerializeField] private Sprite m_spriteUnlocked;
	[SerializeField] private Sprite m_spriteOpen; // TODO: use animation?
	[SerializeField] private WeightedObject<AudioClip>[] m_openSFX;
	[SerializeField] private float m_openSizePct = 0.5f;


	public GameObject Parent { get => null; set => Debug.LogError("Cannot set ChestController.Parent"); }
	public bool IsLocked => m_isLocked;


	private bool m_rare;
	private RoomType m_roomType;
	private int m_itemCountExisting;
	private int m_furnitureRemaining;

	private GameObject m_keyObj;
	private bool m_isLocked = true;
	private bool m_isOpen = false;
	private readonly System.Collections.Generic.List<GameObject> m_prespawnedKeys = new();


	public override int SpawnItems(bool rare, RoomType roomType, int itemCountExisting, int furnitureRemaining)
	{
		// store item params for use when opened
		m_rare = rare;
		m_roomType = roomType;
		m_itemCountExisting = itemCountExisting;
		m_furnitureRemaining = furnitureRemaining;

		// defer or spawn
		if (!m_isOpen)
		{
			// determine item count now to "reserve" the proper number w/ our caller
			m_itemsMin = ItemCount(itemCountExisting, furnitureRemaining, roomType);
			m_itemsMax = m_itemsMin;
			return m_itemsMin;
		}
		return base.SpawnItems(rare, roomType, itemCountExisting, furnitureRemaining);
	}

	public override GameObject SpawnKey(GameObject prefab)
	{
		GameObject obj = base.SpawnKey(prefab); // TODO: account for difference in closed/open collider size?
		if (!m_isOpen)
		{
			obj.SetActive(false);
			m_prespawnedKeys.Add(obj);
			--m_itemsMin;
			--m_itemsMax;
		}
		return obj;
	}


	public bool CanInteract(KinematicCharacter interactor) => enabled && !m_isLocked && !m_isOpen;

	public void Interact(KinematicCharacter interactor, bool reverse)
	{
		// TODO: animations/delay/VFX?
		GetComponent<AudioSource>().PlayOneShot(m_openSFX.RandomWeighted());

		// update appearance/size
		GetComponent<SpriteRenderer>().sprite = m_spriteOpen;
		BoxCollider2D collider = GetComponent<BoxCollider2D>();
		collider.size = new(collider.size.x, collider.size.y * m_openSizePct);
		collider.offset = new(collider.offset.x, collider.size.y * 0.5f); // TODO: don't assume collider lower edges are always at y=0?

		// activate any pre-spawned keys
		foreach (GameObject obj in m_prespawnedKeys)
		{
			if (obj == null) // NOTE that this is possible if locks have been unlocked via other means (e.g. guesswork, console command)
			{
				continue;
			}
			obj.SetActive(true);
		}
		m_prespawnedKeys.Clear();

		// spawn furniture items
		m_isOpen = true;
		SpawnItems(m_rare, m_roomType, m_itemCountExisting, m_furnitureRemaining);
	}


	public void SpawnKeysStatic(RoomController lockRoom, RoomController[] keyRooms)
	{
	}

	public void SpawnKeysDynamic(RoomController lockRoom, RoomController[] keyRooms)
	{
		m_keyObj = keyRooms[Random.Range(0, keyRooms.Length)].SpawnKey(m_keyPrefabs.RandomWeighted(), 0.0f, true);
		GetComponent<SpriteRenderer>().color = m_keyObj.GetComponent<SpriteRenderer>().color;
	}

	public bool IsValidNextKey(GameObject obj) => m_keyObj == obj;

	public bool Unlock(IKey key)
	{
		Debug.Assert(key == null || IsValidNextKey(key.Component.gameObject));
		GetComponent<SpriteRenderer>().sprite = m_spriteUnlocked;
		m_isLocked = false;
		if (key != null)
		{
			key.Deactivate();
			GetComponent<AudioSource>().PlayOneShot(m_unlockSFX.RandomWeighted()); // TODO: 'silent' param rather than assuming that non-key unlocks always correlate w/ silent unlocks?
		}
		return true;
	}


	private void OnCollisionEnter2D(Collision2D collision)
	{
		foreach (IKey key in collision.collider.GetComponentsInChildren<IKey>(true))
		{
			if (IsValidNextKey(key.Component.gameObject))
			{
				Unlock(collision.gameObject.GetComponent<IKey>());
				break;
			}
		}
	}
}
