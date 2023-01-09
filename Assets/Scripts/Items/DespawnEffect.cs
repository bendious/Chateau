using System;
using System.Collections.Generic;
using UnityEngine;


[DisallowMultipleComponent]
public class DespawnEffect : MonoBehaviour
{
	[SerializeField] private GameObject m_prefab;
	[SerializeField] private GameObject m_prefabAltRepeated;

	// TODO: split into separate scripts?
	[Serializable] private enum Action
	{
		None,
		TriggerRepeated,
		TriggerOnce,
		TriggerAndDespawn,
	}
	[SerializeField] private Action m_triggerDefault = Action.TriggerOnce;
	[SerializeField] private Action m_triggerEnemy;
	[SerializeField] private Action m_triggerGround;
	[SerializeField] private Action m_triggerWall;


	public KinematicCharacter CauseExternal { private get; set; }


	private KinematicCharacter Cause => CauseExternal != null ? CauseExternal : m_item != null ? m_item.Cause : null; // TODO: track last damage source independent of ItemController?


	private ItemController m_item;

	private readonly List<Collider2D> m_collidersToIgnore = new();


	private void Awake()
	{
		m_item = GetComponent<ItemController>();

		KinematicCollision.OnExecute += OnKinematicCollision;
	}

	private void OnTriggerEnter2D(Collider2D collider)
	{
		ProcessCollision(collider, collider.transform.position, Vector2.zero, Vector2.zero); // TODO: better position?
	}

	private void OnCollisionEnter2D(Collision2D collision)
	{
		List<ContactPoint2D> contacts = new();
		collision.GetContacts(contacts);
		ContactPoint2D contact = contacts.SelectMax(contact => contact.normal.y); // TODO: parameterize multi-normal selection/merge?
		ProcessCollision(collision.collider, contact.point, contact.normal, collision.relativeVelocity);
	}

	private void OnCollisionStay2D(Collision2D collision) => OnCollisionEnter2D(collision);

	private void OnDestroy()
	{
		KinematicCollision.OnExecute -= OnKinematicCollision;

		if (m_triggerDefault == Action.None || GameController.IsSceneLoad || gameObject.activeSelf) // NOTE that if the game object is still active, then this script is getting removed while the object is still alive, so we shouldn't activate despawn effects
		{
			return;
		}
		SpawnEffect(m_triggerDefault, transform.position);
	}


	private void OnKinematicCollision(KinematicCollision evt)
	{
		bool isObj1 = evt.m_component1.gameObject == gameObject;
		if (!isObj1 && evt.m_component2.gameObject != gameObject)
		{
			return;
		}

		ProcessCollision((isObj1 ? evt.m_component2 : evt.m_component1).GetComponent<Collider2D>(), evt.m_position, isObj1 ? evt.m_normal : -evt.m_normal, Vector2.zero, evt.m_component1.HasEqualPriority(evt.m_component2));
	}

	private void ProcessCollision(Collider2D collider, Vector3 position, Vector2 normal, Vector2 preCollisionVelocity, bool isEqualPriorityKinematic = false)
	{
		if (m_collidersToIgnore.Contains(collider))
		{
			return;
		}

		Action trigger = Action.None;
		if (m_triggerEnemy != Action.None && Cause != null)
		{
			KinematicCharacter character = collider.GetComponent<KinematicCharacter>();
			if (character != null && character.GetComponent<Health>().IsAlive && Cause.CanDamage(character.gameObject))
			{
				trigger = (Action)Math.Max((byte)trigger, (byte)m_triggerEnemy);
			}
		}

		int groundLayerMask = GameController.Instance.m_layerWalls | GameController.Instance.m_layerOneWay; // TODO: parameterize?
		if (((1 << collider.gameObject.layer) & groundLayerMask) != 0 || isEqualPriorityKinematic) // NOTE the treatment of equal priority kinematic objects as if they were walls to prevent "stuck" accelerators
		{
			KinematicObject kinematicObj = GetComponent<KinematicObject>(); // TODO: cache?
			Rigidbody2D body = GetComponent<Rigidbody2D>(); // TODO: cache?
			Vector2 velocity = preCollisionVelocity != Vector2.zero ? preCollisionVelocity : kinematicObj != null ? (kinematicObj.velocity.sqrMagnitude.FloatEqual(0.0f) ? kinematicObj.TargetVelocity : kinematicObj.velocity) : body != null ? body.velocity : Vector2.zero; // NOTE that we can't rely on KinematicObject.velocity still being set since this is post-collision // TODO: use pre-collision velocity for kinematic collisions, too?
			float dot = Vector2.Dot(normal, velocity);

			// TODO: parameterize thresholds? exclude grazing contacts?
			bool isGroundTrigger = m_triggerGround != Action.None && dot < -0.05f;
			bool isWallTrigger = m_triggerWall != Action.None && dot.FloatEqual(0.0f, 0.05f);
			if (isGroundTrigger || isWallTrigger)
			{
				trigger = (Action)Math.Max((byte)trigger, isGroundTrigger && isWallTrigger ? Math.Max((byte)m_triggerGround, (byte)m_triggerWall) : isGroundTrigger ? (byte)m_triggerGround : (byte)m_triggerWall);
			}
		}

		if (trigger != Action.None)
		{
			SpawnEffect(trigger, position);
			m_triggerDefault = Action.None; // TODO: don't lie to ourselves?

			if (trigger == Action.TriggerOnce || trigger == Action.TriggerAndDespawn)
			{
				m_collidersToIgnore.Add(collider);
			}
			if (trigger == Action.TriggerAndDespawn)
			{
				gameObject.SetActive(false);
				Simulation.Schedule<ObjectDespawn>().m_object = gameObject;
			}
		}
	}

	private void SpawnEffect(Action type, Vector3 position)
	{
		Explosion explosion = Instantiate(type == Action.TriggerRepeated && m_prefabAltRepeated != null ? m_prefabAltRepeated : m_prefab, position, transform.rotation).GetComponent<Explosion>();
		if (explosion != null)
		{
			explosion.m_source = Cause;
		}
	}
}
