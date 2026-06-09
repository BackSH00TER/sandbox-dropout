using Sandbox;

public sealed class Music : Component
{
	[Property] public SoundPointComponent SoundPoint { get; set; }
	[Property] public float FadeInDuration { get; set; } = 3f;
	[Property] public float FadeOutDuration { get; set; } = 3f;
	[Property] public bool AutoFadeIn { get; set; } = true;

	private float mutedVolume = 0f;
	private float maxVolume = 1f;

	public bool IsFadingIn { get; private set; } = false;
	public bool IsFadingOut { get; private set; } = false;
	public bool IsFinished { get; private set; } = false;

	protected override void OnAwake()
	{
		SoundPoint.Volume = mutedVolume;

		if ( AutoFadeIn )
		{
			FadeIn();
		}
	}

	public void FadeIn()
	{
		IsFinished = false;
		IsFadingOut = false;
		IsFadingIn = true;
	}

	public void FadeOut()
	{
		IsFinished = false;
		IsFadingIn = false;
		IsFadingOut = true;
	}

	protected override void OnFixedUpdate()
	{
		if ( IsFadingIn )
		{
			if ( SoundPoint.Volume >= maxVolume )
			{
				IsFadingIn = false;
				IsFinished = true;
				return;
			}

			SoundPoint.Volume = SoundPoint.Volume.LerpTo( maxVolume, Time.Delta / FadeInDuration );
		}
		else if ( IsFadingOut )
		{
			if ( SoundPoint.Volume <= mutedVolume )
			{
				IsFadingOut = false;
				IsFinished = true;
				return;
			}

			SoundPoint.Volume = SoundPoint.Volume.LerpTo( mutedVolume, Time.Delta / FadeOutDuration );
		}
	}
}
