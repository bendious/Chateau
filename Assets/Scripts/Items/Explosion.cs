using UnityEngine;


[DisallowMultipleComponent]
public class Explosion : MonoBehaviour
{
	public float m_radius = -1.0f;

	public float m_damage = 1.0f;

	public WeightedObject<AudioClip>[] m_sfx;

	public KinematicCharacter m_source;


    private void Start()
    {
		// SFX
		AudioSource audio = GetComponent<AudioSource>();
		audio.clip = m_sfx.RandomWeighted();
		audio.Play();

		if (m_radius >= 0.0f)
		{
			// collect-based damage
			Collider2D[] objects = Physics2D.OverlapCircleAll(transform.position, m_radius, Physics2D.GetLayerCollisionMask((m_source == null ? gameObject : m_source.gameObject).layer));
			foreach (Collider2D collider in objects)
			{
				ProcessCollider(collider);
			}
		}
    }

	private void OnTriggerEnter2D(Collider2D collider)
	{
		ProcessCollider(collider);
	}

	private void OnTriggerStay2D(Collider2D collider)
	{
		// TODO: don't tick damage every frame even on characters w/ no invulnerability time?
		ProcessCollider(collider);
	}

#if UNITY_EDITOR
	private void OnDrawGizmos()
	{
		if (ConsoleCommands.ExplosionDebug)
		{
			UnityEditor.Handles.DrawWireArc(transform.position, Vector3.forward, Vector3.right, 360.0f, m_radius);
		}
	}
#endif


	private void ProcessCollider(Collider2D collider)
	{
		if (!collider.gameObject.activeSelf || collider.transform.parent != null) // e.g. items in backpacks
		{
			return;
		}

		// restrict avatar damage to only enemy-caused explosions
		if (m_source == null && GameController.Instance.m_avatars.Exists(avatar => avatar.gameObject == collider.gameObject))
		{
			return;
		}

		Health health = collider.GetComponent<Health>();
		if (health == null)
		{
			return;
		}
		health.Decrement(m_source == null ? gameObject : m_source.gameObject, m_damage);
	}
}