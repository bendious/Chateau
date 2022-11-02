using System;
using System.Linq;
using UnityEngine;


[DisallowMultipleComponent]
public class LineConnector : MonoBehaviour
{
	[SerializeField] private Vector3 m_lineOffset;
	[SerializeField] private float m_lengthMin = 0.1f;
	[SerializeField] private float m_lengthMax = 0.0f;

	[SerializeField] Collider2D[] m_colliders;

	[SerializeField] private float m_collisionVelocityMin = 1.0f;
	[SerializeField] private float m_collisionForceMax = 10.0f;
	[SerializeField] private float m_stretchThreshold = 0.25f;

	[SerializeField] private WeightedObject<AudioClip>[] m_sfxStretch;
	[SerializeField] private WeightedObject<AudioClip>[] m_sfxSnap;

	[SerializeField] private bool m_colorMatching = true;


	private LineRenderer[] m_lines;

	private ArmController m_arm;
	private AnchoredJoint2D[] m_joints;

	private KinematicCharacter m_character;
	private Transform m_parentTfOrig; // stored since transform.parent can be changed due to item attachment/detachment, after which we still want to use the original parent
	private SpriteRenderer m_parentRenderer;

	private AudioSource m_audio;

	private float m_lengthMinSq;
	private float m_lengthMaxSq;
	private float m_collisionForceMaxSq;

	private float m_stretchLenPrev;


	private void Awake()
	{
		m_lines = GetComponentsInChildren<LineRenderer>();
		m_arm = GetComponent<ArmController>();
		m_joints = GetComponents<AnchoredJoint2D>();
		m_parentTfOrig = transform.parent;
		m_character = m_parentTfOrig.GetComponent<KinematicCharacter>();
		m_parentRenderer = (m_character != null ? (Component)m_character : this).GetComponentInChildren<SpriteRenderer>();
		m_audio = GetComponent<AudioSource>();

		m_collisionForceMaxSq = m_collisionForceMax * m_collisionForceMax;

		// set null connections to attach to ceiling
		foreach (AnchoredJoint2D joint in m_joints)
		{
			if (joint is DistanceJoint2D distanceJoint && distanceJoint.distance < Utility.FloatEpsilon)
			{
				joint.connectedAnchor = new(transform.position.x, GameController.Instance.RoomFromPosition(transform.position).BoundsInterior.max.y);
				distanceJoint.distance = Vector2.Distance((Vector2)transform.position + (Vector2)(transform.rotation * joint.anchor), joint.connectedAnchor); // NOTE that connectedAnchor is effectively in worldspace since there is no connected body // TODO: don't assume connection to the world?
				m_lengthMax += distanceJoint.distance; // TODO: better aggregation?
			}
			else if (joint is SpringJoint2D springJoint && springJoint.distance < Utility.FloatEpsilon)
			{
				joint.connectedAnchor = new(transform.position.x, GameController.Instance.RoomFromPosition(transform.position).BoundsInterior.max.y);
				springJoint.distance = Vector2.Distance((Vector2)transform.position + (Vector2)(transform.rotation * joint.anchor), joint.connectedAnchor); // NOTE that connectedAnchor is effectively in worldspace since there is no connected body // TODO: don't assume connection to the world?
				m_lengthMax += springJoint.distance; // TODO: better aggregation?
			}
		}

		m_lengthMinSq = m_lengthMin * m_lengthMin;
		m_lengthMaxSq = m_lengthMax * m_lengthMax;
	}

	private void LateUpdate()
	{
		Quaternion rotChar = m_character == null ? Quaternion.identity : m_character.transform.rotation;
		Vector3 shoulderPosLocal = m_parentTfOrig.position + (rotChar * (m_character != null ? m_character.ArmOffset : Vector2.zero) + (m_arm == null ? Vector3.zero : (Vector3)(Vector2)m_arm.AttachOffsetLocal)) - transform.position; // NOTE the removal of Z from m_arm.AttachOffsetLocal
		Color color = m_parentRenderer.color;
		Lazy<Gradient> newGradient = new(() => new() { colorKeys = new GradientColorKey[] { new(color, 0.0f) }, alphaKeys = new GradientAlphaKey[] { new(color.a, 0.0f) } }, false); // TODO: support non-constant gradients?

		int i = 0;
		foreach (LineRenderer line in m_lines)
		{
			// get joint status
			bool jointExisted = m_joints.Length > i;
			AnchoredJoint2D joint = jointExisted ? m_joints[i] : null;
			if (jointExisted && joint == null)
			{
				// skip over broken joints
				++i;
				continue;
			}

			// calculate start/end
			// TODO: simplify?
			Vector3 offsetOriented = m_arm != null && m_arm.LeftFacing ? -m_lineOffset : m_lineOffset;
			Quaternion rotInvLine = Quaternion.Inverse(line.transform.rotation);
			Vector3 startPosLocal = offsetOriented + (joint == null ? Vector3.zero : (Vector3)joint.anchor);
			Vector3 endPosLocal = rotInvLine * (joint == null ? shoulderPosLocal : (joint.connectedBody == null ? (Vector3)joint.connectedAnchor : joint.connectedBody.transform.position + joint.connectedBody.transform.rotation * joint.connectedAnchor) - transform.position) + offsetOriented;

			// update renderer
			float lengthSq = ((Vector2)endPosLocal - (Vector2)startPosLocal).sqrMagnitude;
			line.enabled = lengthSq >= m_lengthMinSq && (m_lengthMaxSq <= 0.0f || lengthSq <= m_lengthMaxSq);
			if (line.enabled)
			{
				line.SetPosition(0, startPosLocal);
				line.SetPosition(1, endPosLocal);
				if (m_colorMatching && (line.colorGradient.colorKeys.First().color != color || line.colorGradient.alphaKeys.First().alpha != color.a)) // TODO: don't assume a constant gradient across the line?
				{
					line.colorGradient = newGradient.Value; // NOTE that we have to replace the whole gradient rather than just setting individual attributes due to the annoying way LineRenderer prevents those changes
				}
			}

			// update collider
			if (joint != null && i < m_colliders.Length)
			{
				Collider2D collider = m_colliders[i];
				if (collider == null || !collider.enabled || (m_lengthMaxSq > 0.0f && lengthSq > m_lengthMaxSq))
				{
					Destroy(joint);
					RemoveJoint(joint);
					line.enabled = false;
					if (collider != null)
					{
						collider.enabled = false;
					}

					// snap SFX
					if (m_sfxSnap.Length > 0)
					{
						m_audio.clip = m_sfxSnap.RandomWeighted();
						m_audio.Play();
					}
				}
				else
				{
					Vector3 centerLocal = (startPosLocal + endPosLocal) * 0.5f;
					collider.transform.localPosition = centerLocal;
					Vector2 anchorToCenterLocal = (Vector2)centerLocal - joint.anchor;
					collider.transform.localRotation = Utility.ZRotation(anchorToCenterLocal);
					(collider as BoxCollider2D).size = new(anchorToCenterLocal.magnitude * 2.0f, Mathf.Max(0.05f, line.startWidth)); // TODO: don't assume BoxCollider2D? more dynamic minimum?

					// stretch SFX
					if (m_sfxStretch.Length > 0)
					{
						float stretchLen = Mathf.Sqrt(lengthSq);
						if (!stretchLen.FloatEqual(m_stretchLenPrev, m_stretchThreshold))
						{
							if (stretchLen > m_stretchLenPrev)
							{
								if (!m_audio.isPlaying)
								{
									float jointLen = joint is DistanceJoint2D distanceJoint ? distanceJoint.distance : ((SpringJoint2D)joint).distance; // TODO: support other joint types?
									if (stretchLen > jointLen + m_stretchThreshold)
									{
										m_audio.clip = m_sfxStretch.RandomWeighted();
										m_audio.Play();
									}
								}
							}
							else
							{
								m_audio.Stop();
							}
							m_stretchLenPrev = stretchLen;
						}
					}
				}
			}

			++i;
		}
	}

	private void OnTriggerEnter2D(Collider2D collider)
	{
		if (collider.isTrigger || collider.attachedRigidbody == null) // TODO: handle hitting static objects?
		{
			return;
		}
		Rigidbody2D body = GetComponent<Rigidbody2D>();
		if (body == null)
		{
			return;
		}

		// add small force, as if this were an non-trigger collider thin enough to slip past after getting grazed
		KinematicCharacter character = collider.attachedRigidbody.GetComponent<KinematicCharacter>();
		Vector2 velocity = character != null ? character.velocity : collider.attachedRigidbody.velocity;
		if (velocity.sqrMagnitude < m_collisionVelocityMin)
		{
			return;
		}
		Vector2 force = velocity * collider.attachedRigidbody.mass;
		Vector2 forceClamped = force.sqrMagnitude > m_collisionForceMaxSq ? force.normalized * m_collisionForceMax : force;
		body.AddForceAtPosition(forceClamped, body.ClosestPoint(collider.transform.position));
	}

	private void OnJointBreak2D(Joint2D joint)
	{
		AnchoredJoint2D jointAnchored = joint as AnchoredJoint2D;
		Debug.Assert(jointAnchored != null);
		int jointIdx = Array.IndexOf(m_joints, jointAnchored);
		Debug.Assert(jointIdx >= 0 && jointIdx < m_joints.Length);

		RemoveJoint(joint);

		if (jointIdx < m_colliders.Length)
		{
			m_colliders[jointIdx].enabled = false;
		}
	}


	private void RemoveJoint(Joint2D joint)
	{
		// TODO: SFX? split into two lines?

		joint.GetComponentInChildren<UnityEngine.Rendering.Universal.Light2D>().enabled = false; // TODO: move elsewhere?

		if (m_joints.All(existingJoint => existingJoint == joint || existingJoint == null))
		{
			if (transform.parent == m_parentTfOrig)
			{
				transform.SetParent(null);
			}
			enabled = false;
		}
	}
}
