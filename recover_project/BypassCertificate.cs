using UnityEngine.Networking;

public class BypassCertificate : CertificateHandler
{
	public override bool ValidateCertificate(byte[] certificateData)
	{
		return true;
	}
}
