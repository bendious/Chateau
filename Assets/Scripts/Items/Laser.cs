using UnityEngine;
using UnityEngine.Rendering.Universal;


[DisallowMultipleComponent, RequireComponent(typeof(Light2D))]
public class Laser : MonoBehaviour
{
	[SerializeField] private float m_warmupSeconds = 0.5f;
	[SerializeField] private float m_damagePerSecond = 0.5f;
	[SerializeField] private LayerMaskHelper m_layers;
	[SerializeField] private GameObject m_endpointObj;


	private Light2D m_light;
	private float m_rangeMax;
	private ItemController m_item;

	private Collider2D m_target;
	private float m_secondsAccum;


	private void Awake()
	{
		m_endpointObj.transform.SetParent(null);
		m_light = GetComponent<Light2D>();
		m_rangeMax = m_light.pointLightOuterRadius;
		m_item = GetComponentInParent<ItemController>();
	}

	private void OnDisable()
	{
		m_endpointObj.SetActive(false);
		m_target = null;
		m_secondsAccum = 0.0f;
	}

	private void Update()
	{
		// early-outs
		if (!m_light.isActiveAndEnabled)
		{
			OnDisable();
			return;
		}
		if (Time.deltaTime <= 0.0f)
		{
			return;
		}

		// raycast & accumulate time/damage
		RaycastHit2D hit = Physics2D.Raycast(transform.position, transform.rotation * Vector2.up, m_rangeMax, m_layers);
		if (hit.collider != null && hit.collider == m_target)
		{
			m_secondsAccum += Time.deltaTime;
			if (m_secondsAccum > m_warmupSeconds)
			{
				// TODO: cache health rather than collider? combine w/ ItemController logic?
				Health health = m_target.GetComponent<Health>();
				if (health == null && m_target.attachedRigidbody != null && m_target.attachedRigidbody.gameObject != m_target.gameObject)
				{
					health = m_target.attachedRigidbody.GetComponent<Health>();
				}
				if (health != null)
				{
					health.Decrement(m_item.Cause.gameObject, m_damagePerSecond * Time.deltaTime);
				}
			}
		}
		else
		{
			m_secondsAccum = 0.0f;
		}
		m_target = hit.collider;

		// update visuals
		if (hit.collider == null)
		{
			m_endpointObj.SetActive(false);
		}
		else
		{
			m_endpointObj.SetActive(true);
			m_endpointObj.transform.position = hit.point;
		}
		m_light.pointLightOuterRadius = hit.collider != null ? hit.distance : m_rangeMax;
	}

	private void OnDestroy()
	{
		if (GameController.IsSceneLoad)
		{
			return; // m_endpointObj will be or has already been cleaned up via scene unload
		}
		Simulation.Schedule<ObjectDespawn>().m_object = m_endpointObj;
	}
}
