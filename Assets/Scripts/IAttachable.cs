using UnityEngine;
using UnityEngine.SceneManagement;


public interface IAttachable
{
	public Component Component => this as Component;

	// NOTE that there is no Attach() since the entry point is IHolder.ChildAttach()
	public void Detach(bool noAutoReplace);

	// this (although public) should only be called by IHolder.ChildAttach{Internal}() // TODO?
	public void AttachInternal(IHolder holder);


	public static void AttachInternalShared(IAttachable attachable, IHolder holder, Rigidbody2D body)
	{
		Component attachableComp = attachable.Component;
		Transform attachableTf = attachableComp.transform;
		if (attachableTf.parent != null)
		{
			// ensure any special detach logic gets invoked
			attachable.Detach(false);
		}

		Component holderComp = holder.Component;
		attachableTf.SetParent(holderComp.transform);
		attachableTf.localPosition = holder.ChildAttachPointLocal; // TODO: lerp?
		attachableTf.localRotation = Quaternion.identity; // TODO: lerp?

		body.velocity = Vector2.zero;
		body.angularVelocity = 0.0f;
		body.bodyType = RigidbodyType2D.Kinematic;

		attachableComp.gameObject.layer = holderComp.gameObject.layer;

		AvatarController avatar = holderComp.GetComponent<AvatarController>();
		if (avatar != null)
		{
			avatar.InventorySync();
		}
	}

	public static void DetachInternalShared(GameObject attachableObj)
	{
		attachableObj.transform.SetParent(null);

		Rigidbody2D body = attachableObj.GetComponent<Rigidbody2D>();
		body.bodyType = RigidbodyType2D.Dynamic;
		body.WakeUp();

		// "cancel" the effect of DontDestroyOnLoad() in case we were attached to the avatar
		// see https://answers.unity.com/questions/1491238/undo-dontdestroyonload.html
		SceneManager.MoveGameObjectToScene(attachableObj, SceneManager.GetActiveScene());
	}
}
