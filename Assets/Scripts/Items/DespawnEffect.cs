using UnityEngine;


[DisallowMultipleComponent]
public class DespawnEffect : MonoBehaviour
{
	[SerializeField] private GameObject m_prefab;

	// TODO: split into separate scripts?
	[SerializeField] private bool m_enemyAutoTrigger;
	[SerializeField] private bool m_groundAutoTrigger;
	[SerializeField] private float m_groundSnapDistMax;


	public KinematicCharacter CauseExternal { private get; set; }


	private KinematicCharacter Cause => CauseExternal != null ? CauseExternal : m_item != null ? m_item.Cause : null; // TODO: track last damage source independent of ItemController?


	private ItemController m_item;


	private void Awake()
	{
		m_item = GetComponent<ItemController>();
	}

	private void OnTriggerEnter2D(Collider2D collider)
	{
		ProcessCollision(collider, Vector2.zero);
	}

	private void OnCollisionEnter2D(Collision2D collision)
	{
		System.Collections.Generic.List<ContactPoint2D> contacts = new();
		collision.GetContacts(contacts);
		ProcessCollision(collision.collider, contacts.SelectMax(contact => contact.normal.y).normal); // TODO: parameterize multi-normal selection/merge?
	}

	private void OnDestroy()
	{
		if (GameController.IsSceneLoad || gameObject.activeSelf) // NOTE that if the game object is still active, then this script is getting removed while the object is still alive, so we shouldn't activate despawn effects
		{
			return;
		}

		Explosion explosion = Instantiate(m_prefab, transform.position, transform.rotation).GetComponent<Explosion>();
		if (explosion != null)
		{
			explosion.m_source = Cause;
		}
	}


	private void ProcessCollision(Collider2D collider, Vector2 normal)
	{
		bool trigger = false;
		bool atGround = false;
		if (m_enemyAutoTrigger && Cause != null)
		{
			KinematicCharacter character = collider.GetComponent<KinematicCharacter>();
			if (character != null && character.GetComponent<Health>().IsAlive && Cause.CanDamage(character.gameObject))
			{
				trigger = true;
			}
		}

		int groundLayerMask = GameController.Instance.m_layerWalls | GameController.Instance.m_layerOneWay; // TODO: parameterize?
		if (m_groundAutoTrigger && ((1 << collider.gameObject.layer) & groundLayerMask) != 0 && Vector2.Dot(normal, Vector2.up) > 0.0f) // TODO: parameterize ground normal threshold?
		{
			trigger = true;
			atGround = true;
		}

		if (trigger)
		{
			// if ground-based and near the ground, snap there
			if (m_groundSnapDistMax > 0.0f && !atGround)
			{
				RaycastHit2D groundCheck = Physics2D.Raycast(transform.position, Vector2.down, m_groundSnapDistMax, groundLayerMask); // TODO: cast whole collider/body?
				if (groundCheck.collider != null)
				{
					transform.position = groundCheck.point;
				}
			}

			gameObject.SetActive(false);
			Simulation.Schedule<ObjectDespawn>().m_object = gameObject;
		}
	}
}
