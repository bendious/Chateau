using UnityEngine;


public class ClothController : MonoBehaviour
{
	public string m_name;
	[SerializeField] private Vector3 m_offset;
	[SerializeField] private bool m_offsetFromHead;


	private Cloth m_cloth;
	private KinematicCharacter m_character;

	private bool m_wasLeftFacing;


	private void Awake() => m_cloth = GetComponent<Cloth>();

	private void Start()
	{
		// TODO: handle attachment/detachment?
		if (transform.parent != null)
		{
			m_character = GetComponentInParent<KinematicCharacter>();
			if (m_character != null)
			{
				m_cloth.capsuleColliders = m_character.GetComponentsInChildren<CapsuleCollider>(); // TODO: also support sphere collider pairs? don't stomp existing capsuleColliders[] entries?

				// prevent double-clothing
				foreach (ClothController otherCloth in m_character.GetComponentsInChildren<ClothController>())
				{
					if (otherCloth != this && otherCloth.m_offsetFromHead == m_offsetFromHead) // TODO: don't assume that only one clothing item is ever allowed per "slot"?
					{
						// TODO: detach rather than despawning?
						Simulation.Schedule<ObjectDespawn>().m_object = otherCloth.gameObject;
					}
				}
			}
		}
	}

	private void LateUpdate()
	{
		gameObject.transform.localPosition = (Vector3)(m_offsetFromHead ? m_character.HeadOffset : m_character.ArmOffset) + (m_character.LeftFacing ? new(-m_offset.x, m_offset.y, m_offset.z) : m_offset);
		if (m_wasLeftFacing != m_character.LeftFacing)
		{
			gameObject.transform.localRotation = m_character.LeftFacing ? Quaternion.Euler(0.0f, 180.0f, 0.0f) : Quaternion.identity;
			m_wasLeftFacing = m_character.LeftFacing;
		}
	}
}
