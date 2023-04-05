using UnityEngine;


[DisallowMultipleComponent, RequireComponent(typeof(Collider2D))]
public class InteractChute : MonoBehaviour, IInteractable, IDespawner
{
	[SerializeField] private Dialogue m_dialogue;


	private SpriteRenderer m_renderer;

	private Vector3 m_offsetToTop;

	private Transform m_despawnTf = null;
	private float m_despawnVel = -1.0f;


	private void Start()
	{
		m_renderer = GetComponent<SpriteRenderer>();
		m_offsetToTop = SpriteOffsetFromPivot(m_renderer.sprite, new Vector2(0.5f, 1.0f));
	}

	private void FixedUpdate()
	{
		if (m_despawnTf == null)
		{
			return;
		}

		// animate as if falling
		m_despawnVel += Mathf.Abs(Physics2D.gravity.y) * Time.fixedDeltaTime;
		Vector3 localPos = m_despawnTf.localPosition;
		localPos.y -= m_despawnVel * Time.fixedDeltaTime;
		m_despawnTf.localPosition = localPos;
		// TODO: update percentage of SpriteRenderer visible if it ever supports partial-fill rendering like UI.Image does
	}


	public bool CanInteract(KinematicCharacter interactor) => enabled && m_despawnTf == null && !GameController.Instance.m_dialogueController.IsPlaying && !GameController.Instance.ActiveEnemiesRemain();

	public void Interact(KinematicCharacter interactor, bool reverse)
	{
		GameController.Instance.m_dialogueController.Play(m_dialogue.m_dialogue.RandomWeighted().m_lines, target: interactor.GetComponent<KinematicCharacter>(), sourceMain: this, expressionSets: m_dialogue.m_expressions);
	}


	public void DespawnAttachable(IAttachable attachable) => StartCoroutine(DespawnAttachableCoroutine(attachable));


	// TODO: combine w/ InteractFollow.VoronoiMasks() logic?
	private Vector3 SpriteOffsetFromPivot(Sprite sprite, Vector2 uvTarget)
	{
		Vector2 offsetUV = uvTarget - sprite.pivot / sprite.rect.size;
		return offsetUV * sprite.rect.size / sprite.pixelsPerUnit;
	}

	private System.Collections.IEnumerator DespawnAttachableCoroutine(IAttachable attachable)
	{
		attachable.Detach(false); // NOTE that this is BEFORE collecting object info/colliders

		// get/calculate object info
		Component attachableComp = attachable.Component;
		Collider2D[] attachableColliders = attachableComp.GetComponentsInChildren<Collider2D>();
		Vector3 attachableExtents = attachableColliders.ToBounds().extents;
		bool isWide = attachableExtents.x > attachableExtents.y;
		bool upsideDown = Random.value > 0.5f;
		Quaternion localRotation = isWide || upsideDown ? Quaternion.Euler(0.0f, 0.0f, upsideDown ? (isWide ? -90.0f : 180.0f) : 90.0f) : Quaternion.identity;
		SpriteRenderer attachableRenderer = attachableComp.GetComponent<SpriteRenderer>();
		Vector3 attachablePivotOffset = localRotation * SpriteOffsetFromPivot(attachableRenderer.sprite, Quaternion.Inverse(localRotation) * new Vector2(0.0f, -0.5f) + new Vector3(0.5f, 0.5f)); // NOTE the inverse rotation of the UVs from the center rather than the origin

		// disable
		(attachableComp as Behaviour).enabled = false;
		foreach (Collider2D c in attachableColliders)
		{
			c.enabled = false;
			c.attachedRigidbody.simulated = false;
		}

		// reparent
		Transform attachableTf = attachableComp.transform;
		attachableTf.SetParent(transform);
		Vector3 localPos = m_offsetToTop - attachablePivotOffset;
		attachableTf.SetLocalPositionAndRotation(localPos, localRotation);

		// ensure drawing behind chute
		// TODO: handle objects wider than chute?
		attachableRenderer.sortingLayerID = m_renderer.sortingLayerID;
		attachableRenderer.sortingOrder = m_renderer.sortingLayerID - 1;
		attachableRenderer.flipX = false;
		attachableRenderer.flipY = false;

		// TODO: animate chute open

		// TODO: drop/fall SFX?

		// wait for animation
		m_despawnTf = attachableTf;
		m_despawnVel = 0.0f;
		float finalY = localPos.y - attachableExtents.y * 2.0f;
		yield return new WaitWhile(() => m_despawnTf.localPosition.y > finalY);
		m_despawnTf = null;
		m_despawnVel = -1.0f;

		// TODO: animate chute close

		Simulation.Schedule<ObjectDespawn>().m_object = attachable.Component.gameObject;
	}
}
