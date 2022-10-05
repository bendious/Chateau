using UnityEngine;


[DisallowMultipleComponent, RequireComponent(typeof(Health))]
public class HiddenDestructible : MonoBehaviour
{
	[SerializeField] private GameObject m_hiddenPrefab;

	[SerializeField] private float m_hiddenPct = 0.25f;


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

		m_hiddenObject = Instantiate(m_hiddenPrefab, transform.position, transform.rotation, transform);

		FurnitureController hiddenFurniture = m_hiddenObject.GetComponentInChildren<FurnitureController>(true);
		if (hiddenFurniture != null)
		{
			hiddenFurniture.RandomizeSize(GetComponent<SpriteRenderer>().bounds.extents);
		}

		m_hiddenObject.SetActive(false);
	}

	private void OnDestroy()
	{
		if (GameController.IsSceneLoad || m_hiddenObject == null)
		{
			return;
		}
		m_hiddenObject.transform.SetParent(transform.parent);

		// unfortunately, since destruction has already started, child components have been deactivated, so we have to go through and re-enable them
		// TODO: exclude components that were already disabled?
		foreach (Behaviour component in m_hiddenObject.GetComponents<Behaviour>())
		{
			component.enabled = true;
		}

		m_hiddenObject.SetActive(true);
	}
}
