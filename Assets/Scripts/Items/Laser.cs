using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;


[DisallowMultipleComponent, RequireComponent(typeof(Light2D))]
public class Laser : MonoBehaviour
{
	[SerializeField] private float m_warmupSeconds = 0.5f;
	[SerializeField] private float m_damagePerSecond = 0.5f;
	[SerializeField] private LayerMaskHelper m_layers;
	[SerializeField] private Light2D m_endpointLight;
	[SerializeField] private UnityEngine.VFX.VisualEffect m_endpointVFX;


	private Light2D m_light;
	public float RangeMax { get; private set; }
	private ItemController m_item;

	private Health m_target;
	private float m_secondsAccum;
	private GameObject m_causeObjMostRecent;

	private Coroutine m_softstop;


	private void Awake()
	{
		m_endpointLight.transform.SetParent(null);
		m_endpointVFX.transform.SetParent(null);
		m_light = GetComponent<Light2D>();
		RangeMax = m_light.pointLightOuterRadius;
		m_item = GetComponentInParent<ItemController>();
		m_causeObjMostRecent = gameObject;
	}

	private void OnDisable()
	{
		m_target = null;
		m_secondsAccum = 0.0f;
		m_causeObjMostRecent = gameObject;

		m_endpointLight.enabled = false;

		if (m_softstop != null)
		{
			StopCoroutine(m_softstop);
		}
		MonoBehaviour vfxCoroutineComp = m_endpointVFX.GetComponents<MonoBehaviour>().FirstOrDefault(c => c.isActiveAndEnabled); // NOTE that VisualEffect does not support StartCoroutine() itself since it is not based on MonoBehaviour
		if (vfxCoroutineComp != null)
		{
			m_softstop = vfxCoroutineComp.StartCoroutine(m_endpointVFX.SoftStop(() => m_target != null, wholeObject: false));
		}
		else
		{
			m_endpointVFX.enabled = false;
		}
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

		// track damage source in case we are dropped while enabled
		if (m_item.Cause != null)
		{
			m_causeObjMostRecent = m_item.Cause.gameObject;
		}

		// raycast & accumulate time/damage
		RaycastHit2D hit = Physics2D.Raycast(transform.position, transform.rotation * Vector2.up, RangeMax, m_layers);
		Health targetPrev = m_target;
		m_target = hit.collider.ToHealth(); // TODO: short grace period before switching/clearing targets?
		if (m_target != null && m_target == targetPrev)
		{
			m_secondsAccum += Time.deltaTime;
			if (m_secondsAccum > m_warmupSeconds)
			{
				m_target.Decrement(m_causeObjMostRecent, m_damagePerSecond * Time.deltaTime);
			}
		}
		else
		{
			m_secondsAccum = 0.0f;
		}

		// update lights
		if (hit.collider == null)
		{
			m_endpointLight.enabled = false;
		}
		else
		{
			m_endpointLight.enabled = true;
			m_endpointLight.transform.position = hit.point;
		}
		m_light.pointLightOuterRadius = hit.collider != null ? hit.distance : RangeMax;

		// update VFX
		if (m_target == null && m_softstop == null)
		{
			m_softstop = StartCoroutine(m_endpointVFX.SoftStop(() => m_target != null, wholeObject: false));
		}
		else
		{
			m_softstop = null;
			if (!m_endpointVFX.enabled)
			{
				m_endpointVFX.enabled = true;
				m_endpointVFX.Play();
			}
			m_endpointVFX.transform.position = hit.point;
		}
	}

	private void OnDestroy()
	{
		if (GameController.IsSceneLoad)
		{
			return; // m_endpointObj will be or has already been cleaned up via scene unload
		}
		Simulation.Schedule<ObjectDespawn>().m_object = m_endpointLight.gameObject;
		if (m_endpointVFX.gameObject != m_endpointLight.gameObject)
		{
			Simulation.Schedule<ObjectDespawn>().m_object = m_endpointVFX.gameObject;
		}
	}
}
