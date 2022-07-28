using System.Linq;
using UnityEngine;


[DisallowMultipleComponent]
public class LineConnector : MonoBehaviour
{
	public Vector3 m_lineOffset;
	public float m_lengthMin = 0.05f;


	private LineRenderer[] m_lines;

	private ArmController m_arm;
	private AnchoredJoint2D[] m_joints;

	private KinematicCharacter m_character;
	private SpriteRenderer m_parentRenderer;


	private void Start()
	{
		m_lines = GetComponentsInChildren<LineRenderer>();
		m_arm = GetComponent<ArmController>();
		m_joints = GetComponents<AnchoredJoint2D>();
		m_character = transform.parent.GetComponent<KinematicCharacter>();
		m_parentRenderer = (m_character != null ? (Component)m_character : this).GetComponent<SpriteRenderer>();
	}

	private void LateUpdate()
	{
		Quaternion rotChar = m_character == null ? Quaternion.identity : m_character.transform.rotation;
		Vector3 shoulderPosLocal = transform.parent.position + (rotChar * (m_character != null ? m_character.ArmOffset : Vector2.zero) + (m_arm == null ? Vector3.zero : (Vector3)(Vector2)m_arm.m_offset)) - transform.position; // NOTE the removal of Z from m_arm.m_offset
		Color color = m_parentRenderer.color;
		System.Lazy<Gradient> newGradient = new(() => new Gradient() { colorKeys = new GradientColorKey[] { new GradientColorKey(color, 0.0f) }, alphaKeys = new GradientAlphaKey[] { new GradientAlphaKey(color.a, 0.0f) } }, false); // TODO: support non-constant gradients?

		int i = 0;
		foreach (LineRenderer line in m_lines)
		{
			// calculate start/end
			// TODO: simplify?
			Vector3 offsetOriented = m_arm != null && m_arm.LeftFacing ? -m_lineOffset : m_lineOffset;
			Quaternion rotInvLine = Quaternion.Inverse(line.transform.rotation);
			AnchoredJoint2D joint = m_joints.Length > i ? m_joints[i] : null;
			Vector3 startPosLocal = offsetOriented + (joint == null ? Vector3.zero : (Vector3)joint.anchor);
			Vector3 endPosLocal = rotInvLine * (joint == null ? shoulderPosLocal : (joint.connectedBody == null ? (Vector3)joint.connectedAnchor : joint.connectedBody.transform.position + joint.connectedBody.transform.rotation * joint.connectedAnchor) - transform.position) + offsetOriented;

			// update renderer
			line.enabled = ((Vector2)endPosLocal - (Vector2)startPosLocal).sqrMagnitude >= m_lengthMin * m_lengthMin;
			if (line.enabled)
			{
				line.SetPosition(0, startPosLocal);
				line.SetPosition(1, endPosLocal);
				if (line.colorGradient.colorKeys.First().color != color || line.colorGradient.alphaKeys.First().alpha != color.a) // TODO: don't assume a constant gradient across the line?
				{
					line.colorGradient = newGradient.Value; // NOTE that we have to replace the whole gradient rather than just setting individual attributes due to the annoying way LineRenderer prevents those changes
				}
			}

			++i;
		}
	}
}
