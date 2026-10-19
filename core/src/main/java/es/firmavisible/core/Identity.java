package es.firmavisible.core;

import java.io.InputStream;
import java.security.*;
import java.security.cert.*;
import java.security.cert.Certificate;
import java.util.*;
import org.bouncycastle.jce.provider.BouncyCastleProvider;
import org.bouncycastle.asn1.x500.X500Name;
import org.bouncycastle.asn1.x500.style.BCStyle;

/** A handle to a private key. Never serializes or logs the key/password. */
public final class Identity {
    private final PrivateKey key;
    private final X509Certificate[] chain;
    public Identity(PrivateKey key, X509Certificate[] chain) {
        if (key == null || chain == null || chain.length == 0) throw new IllegalArgumentException("Falta la clave privada o el certificado.");
        this.key = key; this.chain = chain.clone();
    }
    public PrivateKey key() { return key; }
    public X509Certificate[] chain() { return chain.clone(); }
    public X509Certificate certificate() { return chain[0]; }
    public String name() {
        var names = new X500Name(certificate().getSubjectX500Principal().getName()).getRDNs(BCStyle.CN);
        return names.length == 0 ? certificate().getSubjectX500Principal().getName() : names[0].getFirst().getValue().toString();
    }
    public String description() { return name() + " · caduca " + certificate().getNotAfter().toInstant().atZone(java.time.ZoneId.systemDefault()).toLocalDate(); }
    @Override public String toString() { return description(); }
    public void checkUsable() throws GeneralSecurityException {
        certificate().checkValidity();
        boolean[] usage = certificate().getKeyUsage();
        if (usage != null && !(usage[0] || (usage.length > 1 && usage[1])))
            throw new GeneralSecurityException("El certificado no permite firmar documentos.");
        PublicKey pub = certificate().getPublicKey();
        if (pub instanceof java.security.interfaces.RSAPublicKey && ((java.security.interfaces.RSAPublicKey) pub).getModulus().bitLength() < 2048)
            throw new GeneralSecurityException("Clave RSA inferior a 2048 bits.");
        if (pub instanceof java.security.interfaces.ECPublicKey && ((java.security.interfaces.ECPublicKey) pub).getParams().getCurve().getField().getFieldSize() < 256)
            throw new GeneralSecurityException("Clave EC inferior a 256 bits.");
        algorithm();
    }
    public String algorithm() throws GeneralSecurityException {
        switch (key.getAlgorithm().toUpperCase(Locale.ROOT)) {
            case "RSA": return "SHA256withRSA";
            case "EC": case "ECDSA": return "SHA256withECDSA";
            default: throw new GeneralSecurityException("Tipo de clave no admitido: " + key.getAlgorithm());
        }
    }
    public static List<Identity> fromPkcs12(InputStream input, char[] password) throws Exception {
        KeyStore store = KeyStore.getInstance("PKCS12", new BouncyCastleProvider());
        store.load(input, password);
        return fromStore(store, password);
    }
    public static List<Identity> fromStore(KeyStore store, char[] password) throws Exception {
        List<Identity> identities = new ArrayList<>();
        Enumeration<String> aliases = store.aliases();
        while (aliases.hasMoreElements()) {
            String alias = aliases.nextElement();
            if (!store.isKeyEntry(alias)) continue;
            Key key = store.getKey(alias, password);
            Certificate[] certificates = store.getCertificateChain(alias);
            if (!(key instanceof PrivateKey) || certificates == null || certificates.length == 0) continue;
            X509Certificate[] chain = Arrays.copyOf(certificates, certificates.length, X509Certificate[].class);
            identities.add(new Identity((PrivateKey) key, chain));
        }
        if (identities.isEmpty()) throw new GeneralSecurityException("No se encontró un certificado con su clave privada.");
        return identities;
    }
}
