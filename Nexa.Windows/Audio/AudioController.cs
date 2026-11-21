using NAudio.CoreAudioApi;

namespace Nexa.Windows.Audio;

/// <summary>
/// Controls Windows audio volume using NAudio.
/// </summary>
public class AudioController
{
    private MMDevice? GetDefaultAudioDevice()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }
        catch
        {
            return null;
        }
    }

    public Task<bool> SetVolumeAsync(int level)
    {
        if (level < 0 || level > 100)
        {
            return Task.FromResult(false);
        }

        try
        {
            var device = GetDefaultAudioDevice();
            if (device == null) return Task.FromResult(false);

            device.AudioEndpointVolume.MasterVolumeLevelScalar = level / 100.0f;
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public Task<int> GetVolumeAsync()
    {
        try
        {
            var device = GetDefaultAudioDevice();
            if (device == null) return Task.FromResult(0);

            var volume = (int)(device.AudioEndpointVolume.MasterVolumeLevelScalar * 100);
            return Task.FromResult(volume);
        }
        catch
        {
            return Task.FromResult(0);
        }
    }

    public Task<bool> MuteAsync(bool mute)
    {
        try
        {
            var device = GetDefaultAudioDevice();
            if (device == null) return Task.FromResult(false);

            device.AudioEndpointVolume.Mute = mute;
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public Task<bool> IsMutedAsync()
    {
        try
        {
            var device = GetDefaultAudioDevice();
            if (device == null) return Task.FromResult(false);

            return Task.FromResult(device.AudioEndpointVolume.Mute);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }
}
