using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.VFX;


/// <summary>
/// This is the main class used to implement control of the avatar.
/// </summary>
[RequireComponent(typeof(Health))]
public class AvatarController : KinematicCharacter
{
	public enum JumpState
	{
		Grounded,
		PrepareToJump,
		Jumping,
		InFlight,
		WallCling,
		Landed
	}


	public AudioClip jumpAudio;
	public AudioClip respawnAudio;

	public GameObject m_focusIndicator;
	public GameObject m_focusPrompt;
	public GameObject m_aimObject;
	public GameObject m_inventoryUI;
	public Canvas m_overlayCanvas;

	public Vector3 m_focusPromptOffset = new(0.0f, 0.15f, -0.15f);

	public float m_secondaryDegrees = -45.0f;

	public float m_coyoteTime = 0.15f;

	public Camera m_camera;


	public PlayerControls Controls { get; private set; }

	public bool IsAlive => ConsoleCommands.NeverDie || health.IsAlive;


	private JumpState jumpState = JumpState.Grounded;
	private Health health;
	public bool controlEnabled = true;


	private float m_leftGroundTime = -1.0f;

	private GameObject m_focusObj;

	private VisualEffect m_aimVfx;
	private bool m_aiming;

	private bool m_usingMouse;
	private Vector2 m_mousePosPixels;
	private Vector2 m_joystickDirNonzero;

	// TODO: class for ease of VFX ID use?
	private static int m_spriteID;
	private static int m_sizeID;
	private static int m_spawnOffsetID;
	private static int m_speedID;
	private static int m_forwardID;


	protected override void Awake()
	{
		base.Awake();

		m_focusIndicator.transform.SetParent(null);
		m_focusPrompt.transform.SetParent(null);
		m_aimObject.transform.SetParent(null);

		health = GetComponent<Health>();

		m_aimVfx = GetComponent<VisualEffect>();
		m_spriteID = Shader.PropertyToID("Sprite");
		m_sizeID = Shader.PropertyToID("Size");
		m_spawnOffsetID = Shader.PropertyToID("SpawnOffset");
		m_speedID = Shader.PropertyToID("Speed");
		m_forwardID = Shader.PropertyToID("Forward");

		InventorySync();

		Controls = new();

		ObjectDespawn.OnExecute += OnObjectDespawn;
	}

	private void OnDisable()
	{
		Controls.Disable();
	}

	private void OnEnable()
	{
		Controls.Enable();
	}


	protected override void Update()
	{
		if (controlEnabled)
		{
			// collect possible focus objects
			m_focusObj = null;
			float focusRadius = ((CircleCollider2D)m_collider).radius;
			Vector2 mousePos = m_aimObject.transform.position;
			Collider2D[] focusCandidates = Physics2D.OverlapCircleAll((Vector2)transform.position + (mousePos - (Vector2)transform.position).normalized * focusRadius, focusRadius * 1.5f); // TODO: restrict to certain layers?

			// determine current focus object
			// TODO: more nuanced prioritization?
			float distSqFocus = float.MaxValue;
			bool focusCanInteract = false;
			foreach (Collider2D candidate in focusCandidates)
			{
				if (ShouldIgnore(candidate.GetComponent<Rigidbody2D>(), new Collider2D[] { candidate }, false, false, false))
				{
					continue; // ignore ourself / attached/ignored objects
				}

				// prioritize interactable objects
				IInteractable candidateInteract = candidate.GetComponent<IInteractable>();
				bool candidateCanInteract = candidateInteract != null && candidateInteract.CanInteract(this);

				// prioritize by mouse position
				float distSqCur = (mousePos - (Vector2)candidate.transform.position).sqrMagnitude;

				if (candidateCanInteract && !focusCanInteract || ((candidateCanInteract || !focusCanInteract) && distSqCur < distSqFocus))
				{
					focusCanInteract = candidateCanInteract;
					distSqFocus = distSqCur;
					m_focusObj = candidate.gameObject;
				}
			}

			// place focus indicator if appropriate
			if (focusCanInteract)
			{
				m_focusIndicator.transform.SetPositionAndRotation(m_focusObj.transform.position + Vector3.back, m_focusObj.transform.rotation); // NOTE the Z offset to ensure the focus indicator is rendered on top
				SpriteRenderer rendererIndicator = m_focusIndicator.GetComponent<SpriteRenderer>();
				SpriteRenderer rendererOrig = m_focusObj.GetComponent<SpriteRenderer>();
				rendererIndicator.sprite = rendererOrig.sprite;
				rendererIndicator.flipY = rendererOrig.flipY; // NOTE that items that have been dropped may have been left "backwards"
				rendererIndicator.drawMode = rendererOrig.drawMode;
				rendererIndicator.size = rendererOrig.size;
				m_focusIndicator.transform.localScale = m_focusObj.transform.localScale; // NOTE that w/o this, swapping between renderer draw modes was doing weird things to the indicator's scale...

				m_focusPrompt.transform.position = m_focusIndicator.transform.position + m_focusPromptOffset;
			}
			m_focusIndicator.SetActive(focusCanInteract);
			m_focusPrompt.SetActive(focusCanInteract);
		}
		else
		{
			move.x = 0;
		}
		UpdateJumpState();
		base.Update();
	}

	protected override void FixedUpdate()
	{
		if (controlEnabled)
		{
			// determine aim position(s)
			Vector2 aimPctsFromCenter = m_joystickDirNonzero;
			if (m_usingMouse)
			{
				Rect cameraRectPixels = m_camera.rect;
				Vector2 screenSize = new(Screen.width, Screen.height);
				cameraRectPixels.position *= screenSize;
				cameraRectPixels.size *= screenSize;
				aimPctsFromCenter = (m_mousePosPixels - cameraRectPixels.center) / (cameraRectPixels.size * 0.5f);
			}
			aimPctsFromCenter.x = Mathf.Clamp(aimPctsFromCenter.x, -1.0f, 1.0f);
			aimPctsFromCenter.y = Mathf.Clamp(aimPctsFromCenter.y, -1.0f, 1.0f);
			Vector2 screenExtentsWS = new(m_camera.orthographicSize * m_camera.aspect, m_camera.orthographicSize);
			Vector2 aimPosConstrained = transform.position + (Vector3)(screenExtentsWS * aimPctsFromCenter);
			Vector2 aimPos = m_usingMouse ? m_camera.ScreenToWorldPoint(m_mousePosPixels) : aimPosConstrained;

			// aim camera/sprite
			m_aimObject.transform.position = aimPosConstrained;
			m_aimDir = aimPos.x > transform.position.x ? 1 : -1;

			// aim arms/items
			// primary aim
			ArmController[] arms = GetComponentsInChildren<ArmController>();
			ArmController primaryArm = arms.Length == 0 ? null : arms.First().transform.childCount > 0 || (arms.Last().transform.childCount == 0 && !LeftFacing) ? arms.First() : arms.Last();
			if (primaryArm != null)
			{
				primaryArm.UpdateAim(m_armOffset, aimPos, aimPos);
			}

			// secondary hold
			Vector3 secondaryAimPos = transform.position + Quaternion.Euler(0.0f, 0.0f, LeftFacing ? 180.0f - m_secondaryDegrees : m_secondaryDegrees) * Vector3.right;
			for (int i = 0; i < arms.Length; ++i)
			{
				if (arms[i] == primaryArm)
				{
					continue;
				}
				arms[i].UpdateAim(m_armOffset, secondaryAimPos, aimPos);
			}

			// aim VFX
			if (m_aimVfx.enabled)
			{
				ItemController primaryItem = GetComponentInChildren<ItemController>();
				m_aimVfx.SetVector3(m_forwardID, (aimPos - (Vector2)transform.position).normalized);
				m_aimVfx.SetVector3(m_spawnOffsetID, primaryItem.transform.position - transform.position + (Vector3)primaryItem.SpritePivotOffset + primaryItem.transform.rotation * primaryItem.m_vfxExtraOffsetLocal);
			}
		}

		base.FixedUpdate();
	}

	private void OnDestroy()
	{
		ObjectDespawn.OnExecute -= OnObjectDespawn;
	}


	// called by InputSystem / PlayerInput component
	public void OnMove(InputValue input)
	{
		if (!controlEnabled)
		{
			return;
		}
		move = m_overlayCanvas.gameObject.activeSelf ? Vector2.zero : input.Get<Vector2>();
	}

	// called by InputSystem / PlayerInput component
	public void OnLook(InputValue input)
	{
		Vector2 value = input.Get<Vector2>();
		if (!controlEnabled || value.sqrMagnitude == 0.0f) // TODO: FloatEqual() despite deadzone?
		{
			return;
		}

		// determine input source
		m_usingMouse = GetComponent<PlayerInput>().currentControlScheme == "Keyboard&Mouse";

		if (m_usingMouse)
		{
			m_mousePosPixels = value;
		}
		else
		{
			m_joystickDirNonzero = value;
		}
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "defined by InputSystem / PlayerInput component")]
	public void OnSwing(InputValue input)
	{
		if (!controlEnabled)
		{
			return;
		}

		ItemController primaryItem = GetComponentInChildren<ItemController>();
		if (primaryItem == null)
		{
			(LeftFacing ? GetComponentsInChildren<ArmController>().Last() : GetComponentInChildren<ArmController>()).Swing();
		}
		else
		{
			primaryItem.Swing();
		}

		// cancel throwing
		// TODO: separate button to cancel a throw?
		StopAiming();
	}

	// called by InputSystem / PlayerInput component
	public void OnThrow(InputValue input)
	{
		ItemController primaryItem = GetComponentInChildren<ItemController>(true);
		if (primaryItem == null)
		{
			return;
		}

		if (input.isPressed)
		{
			// enable/initialize aim VFX
			m_aimVfx.enabled = true;
			Sprite itemSprite = primaryItem.GetComponent<SpriteRenderer>().sprite;
			m_aimVfx.SetTexture(m_spriteID, itemSprite.texture);
			Vector3 itemSize = primaryItem.GetComponent<Collider2D>().bounds.size;
			m_aimVfx.SetFloat(m_sizeID, Mathf.Max(itemSize.x, itemSize.y));
			m_aimVfx.SetFloat(m_speedID, primaryItem.m_throwSpeed);
			m_aiming = true;
		}
		else if (m_aiming)
		{
			StopAiming();
			primaryItem.Throw();
		}
	}

	// called by InputSystem / PlayerInput component
	public void OnJump(InputValue input)
	{
		if (!controlEnabled)
		{
			return;
		}

		if ((jumpState == JumpState.Grounded || jumpState == JumpState.WallCling || m_leftGroundTime + m_coyoteTime <= Time.time) && input.isPressed)
		{
			jumpState = JumpState.PrepareToJump;
		}
		else
		{
			stopJump = true;
		}
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "defined by InputSystem / PlayerInput component")]
	public void OnUse(InputValue input)
	{
		if (!controlEnabled)
		{
			return;
		}

		StopAiming(); // NOTE that we don't care whether the item to be used is currently being aimed or even exists; even if it isn't/doesn't, we treat item use - like swinging - as a throw cancellation

		ItemController[] items = GetComponentsInChildren<ItemController>(true).ToArray();
		foreach (ItemController item in items)
		{
			bool used = item.Use();
			if (used)
			{
				break;
			}
		}
	}

	// called by InputSystem / PlayerInput component
	public void OnInteract(InputValue input)
	{
		if (controlEnabled && input.isPressed && m_focusObj != null)
		{
			IInteractable focusInteract = m_focusObj.GetComponent<IInteractable>();
			if (focusInteract != null && focusInteract.CanInteract(this))
			{
				focusInteract.Interact(this);
			}
		}

		IsPickingUp = input.isPressed && GetComponentsInChildren<ItemController>(true).Length < MaxPickUps;
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "defined by InputSystem / PlayerInput component")]
	public void OnDrop(InputValue input)
	{
		if (!controlEnabled)
		{
			return;
		}

		ItemController primaryItem = GetComponentInChildren<ItemController>(true);
		if (primaryItem != null)
		{
			primaryItem.Detach(false);
		}
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "defined by InputSystem / PlayerInput component")]
	public void OnInventory(InputValue input)
	{
		if (!controlEnabled)
		{
			return;
		}
		m_inventoryUI.SetActive(!m_inventoryUI.activeSelf);
		InventorySync();
	}

	public override void OnDamage(GameObject source)
	{
		base.OnDamage(source);

		if (m_overlayCanvas.gameObject.activeSelf)
		{
			ToggleOverlay(null, null);
		}
	}

	public override bool OnDeath()
	{
		if (ConsoleCommands.NeverDie || !base.OnDeath())
		{
			return false;
		}

		DeactivateAllControl();
		if (GameController.Instance.m_avatars.All(avatar => !avatar.IsAlive))
		{
			Simulation.Schedule<GameOver>(3.0f); // TODO: animation event?
		}

		return true;
	}

	public override bool CanDamage(GameObject target)
	{
		return base.CanDamage(target) && !GameController.Instance.m_avatars.Exists(avatar => avatar.gameObject == target);
	}

	protected override void DespawnSelf()
	{
		// NOTE that we purposely don't call base.DespawnSelf() since the avatar should never despawn
	}

	public void OnEnemyCollision(EnemyController enemy)
	{
		if (!enemy.CanDamage(gameObject))
		{
			return;
		}

		health.Decrement(enemy.gameObject);
		controlEnabled = false; // re-enabled via EnablePlayerControl() animation trigger

		// temporarily disable collision to prevent getting stuck
		EnableCollision.TemporarilyDisableCollision(enemy.GetComponents<Collider2D>(), new Collider2D[] { m_collider });
	}

#if DEBUG
	public void DebugRespawn()
	{
		m_collider.enabled = true;
		body.simulated = true;
		controlEnabled = true;

		if (audioSource && respawnAudio)
		{
			audioSource.PlayOneShot(respawnAudio);
		}

		// TODO: move to animation trigger?
		foreach (ArmController arm in GetComponentsInChildren<ArmController>(true))
		{
			arm.gameObject.SetActive(true);
		}

		health.Respawn();

		Teleport(Vector3.zero);
		m_aimObject.transform.position = Vector3.zero;
		jumpState = JumpState.Grounded;

		animator.SetBool("dead", false);
	}
#endif

	public bool ToggleOverlay(SpriteRenderer sourceRenderer, string text)
	{
		GameObject overlayObj = m_overlayCanvas.gameObject;
		if (!overlayObj.activeSelf)
		{
			Image overlayImage = overlayObj.GetComponentInChildren<Image>();
			overlayImage.sprite = sourceRenderer.sprite;
			overlayImage.color = sourceRenderer.color;
			overlayObj.GetComponentInChildren<TMPro.TMP_Text>().text = text;
		}
		overlayObj.SetActive(!overlayObj.activeSelf);
		return overlayObj.activeSelf;
	}

	// TODO: move to event handler?
	public void InventorySync()
	{
		if (!m_inventoryUI.activeSelf)
		{
			return;
		}

		GameObject templateObj = m_inventoryUI.transform.GetChild(0).gameObject;
		Assert.IsFalse(templateObj.activeSelf);

		// gather all items/slots from child holders
		Tuple<Transform, Color>[] itemInfos = GetComponentsInChildren<IHolder>().SelectMany(holder =>
		{
			Transform holderTf = holder.Component.transform;
			Tuple<Transform, Color>[] children = new Tuple<Transform, Color>[holder.HoldCountMax];
			Color holderColor = holderTf.GetComponent<SpriteRenderer>().color;
			for (int i = 0; i < holder.HoldCountMax; ++i)
			{
				children[i] = Tuple.Create(i < holderTf.childCount ? holderTf.GetChild(i) : null, holderColor);
			}
			return children;
		}).ToArray();

		// create/set one icon per item/slot
		RectTransform templateTf = templateObj.transform.GetComponent<RectTransform>();
		Vector3 posItr = templateTf.anchoredPosition;
		for (int iconIdx = 0; iconIdx < itemInfos.Length; ++iconIdx)
		{
			GameObject UIObj;
			if (iconIdx + 1 < m_inventoryUI.transform.childCount)
			{
				UIObj = m_inventoryUI.transform.GetChild(iconIdx + 1).gameObject;
			}
			else
			{
				posItr.x = templateTf.anchoredPosition.x + (templateTf.sizeDelta.x + templateTf.anchoredPosition.x) * iconIdx;
				UIObj = Instantiate(templateObj, Vector3.zero, Quaternion.identity, m_inventoryUI.transform);
				UIObj.transform.GetComponent<RectTransform>().anchoredPosition = posItr;
				UIObj.SetActive(true);
			}
			Image uiImage = UIObj.GetComponent<Image>();
			Tuple<Transform, Color> itemCur = itemInfos[iconIdx];
			bool nonEmptySlot = itemCur.Item1 != null;
			if (nonEmptySlot)
			{
				SpriteRenderer srcComp = itemCur.Item1.GetComponent<SpriteRenderer>();
				uiImage.sprite = srcComp.sprite;
				uiImage.color = srcComp.color;
			}
			else
			{
				uiImage.sprite = templateObj.GetComponent<Image>().sprite;
				uiImage.color = itemCur.Item2;
			}
			UIObj.GetComponent<InventoryController>().m_draggable = nonEmptySlot;
		}
		for (int j = m_inventoryUI.transform.childCount - 1; j > itemInfos.Length; --j)
		{
			Simulation.Schedule<ObjectDespawn>().m_object = m_inventoryUI.transform.GetChild(j).gameObject;
		}
	}

	public void OnVictory()
	{
		DeactivateAllControl();
		foreach (ArmController arm in GetComponentsInChildren<ArmController>())
		{
			foreach (ItemController item in arm.GetComponentsInChildren<ItemController>())
			{
				item.Detach(true);
			}
			arm.gameObject.SetActive(false);
		}
		animator.SetTrigger("victory");
		GetComponent<Health>().m_invincible = true;
	}


	private void OnObjectDespawn(ObjectDespawn evt)
	{
		if (evt.m_object.transform.root == transform)
		{
			evt.m_object.transform.SetParent(null); // so that we can refresh inventory immediately even though deletion hasn't happened yet
			InventorySync();
		}
	}

	private void DeactivateAllControl()
	{
		controlEnabled = false;
		m_focusIndicator.SetActive(false);
		m_focusPrompt.SetActive(false);
		StopAiming();
	}

	private void StopAiming()
	{
		m_aimVfx.enabled = false; // NOTE that this instantly removes any existing particles, which is fine for this effect
		m_aiming = false;
	}

	private void UpdateJumpState()
	{
		jump = false;
		switch (jumpState)
		{
			case JumpState.Grounded:
				if (!IsGrounded)
				{
					jumpState = JumpState.InFlight;
					m_leftGroundTime = Time.time;
				}
				break;
			case JumpState.PrepareToJump:
				jumpState = JumpState.Jumping;
				jump = true;
				stopJump = false;
				break;
			case JumpState.Jumping:
				if (!IsGrounded)
				{
					if (audioSource && jumpAudio)
					{
						audioSource.PlayOneShot(jumpAudio);
					}
					jumpState = JumpState.InFlight;
				}
				break;
			case JumpState.InFlight:
				if (IsGrounded)
				{
					jumpState = JumpState.Landed;
				}
				else if (IsWallClinging)
				{
					jumpState = JumpState.WallCling;
				}
				break;
			case JumpState.WallCling:
				if (!IsWallClinging)
				{
					jumpState = JumpState.InFlight;
					m_leftGroundTime = Time.time;
				}
				break;
			case JumpState.Landed:
				jumpState = JumpState.Grounded;
				break;
		}
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "called from animation event")]
	private void EnablePlayerControl()
	{
		if (IsAlive)
		{
			controlEnabled = true;
		}
	}
}
