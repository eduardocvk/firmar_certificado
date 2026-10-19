package es.firmavisible.test;

import es.firmavisible.core.Rfc3161Client;
import java.math.BigInteger;
import java.security.*;
import java.security.cert.*;
import java.util.*;
import org.bouncycastle.asn1.x500.X500Name;
import org.bouncycastle.asn1.x509.*;
import org.bouncycastle.asn1.x509.Extension;
import org.bouncycastle.cert.*;
import org.bouncycastle.cert.jcajce.*;
import org.bouncycastle.cms.jcajce.JcaSignerInfoGeneratorBuilder;
import org.bouncycastle.jce.provider.BouncyCastleProvider;
import org.bouncycastle.operator.jcajce.*;
import org.bouncycastle.tsp.*;

/** Synthetic authority used only by JVM/emulator tests; never installed in system trust. */
public final class TestTsa {
    public final X509Certificate root;
    private final X509CertificateHolder signer;
    private final KeyPair key;
    public TestTsa()throws Exception {
        KeyPairGenerator keys=KeyPairGenerator.getInstance("RSA");keys.initialize(2048);KeyPair rootKey=keys.generateKeyPair();key=keys.generateKeyPair();
        X500Name rootName=new X500Name("CN=Test TSA Root - NO REAL"),name=new X500Name("CN=Test Timestamp - NO REAL");
        Date before=new Date(System.currentTimeMillis()-60000),after=new Date(System.currentTimeMillis()+86400000);
        var rootBuilder=new JcaX509v3CertificateBuilder(rootName,BigInteger.ONE,before,after,rootName,rootKey.getPublic());
        rootBuilder.addExtension(Extension.basicConstraints,true,new BasicConstraints(true));rootBuilder.addExtension(Extension.keyUsage,true,new KeyUsage(KeyUsage.keyCertSign|KeyUsage.cRLSign));
        var rootHolder=rootBuilder.build(new JcaContentSignerBuilder("SHA256withRSA").build(rootKey.getPrivate()));
        root=new JcaX509CertificateConverter().setProvider(new BouncyCastleProvider()).getCertificate(rootHolder);
        var builder=new JcaX509v3CertificateBuilder(rootName,BigInteger.TWO,before,after,name,key.getPublic());
        builder.addExtension(Extension.basicConstraints,true,new BasicConstraints(false));
        builder.addExtension(Extension.extendedKeyUsage,true,new ExtendedKeyUsage(KeyPurposeId.id_kp_timeStamping));
        builder.addExtension(Extension.keyUsage,true,new KeyUsage(KeyUsage.digitalSignature));
        signer=builder.build(new JcaContentSignerBuilder("SHA256withRSA").build(rootKey.getPrivate()));
    }
    public Rfc3161Client client(){return new Rfc3161Client(bytes->respond(new TimeStampRequest(bytes)),Collections.singleton(new TrustAnchor(root,null)));}
    public byte[] respond(TimeStampRequest request)throws Exception {
        var digests=new JcaDigestCalculatorProviderBuilder().setProvider(new BouncyCastleProvider()).build();
        var info=new JcaSignerInfoGeneratorBuilder(digests).build(new JcaContentSignerBuilder("SHA256withRSA").build(key.getPrivate()),signer);
        var generator=new TimeStampTokenGenerator(info,digests.get(new AlgorithmIdentifier(org.bouncycastle.asn1.nist.NISTObjectIdentifiers.id_sha256)),new org.bouncycastle.asn1.ASN1ObjectIdentifier("1.2.3.4.1"));
        generator.addCertificates(new org.bouncycastle.cert.jcajce.JcaCertStore(Arrays.asList(root,new JcaX509CertificateConverter().getCertificate(signer))));
        var response=new TimeStampResponseGenerator(generator,TSPAlgorithms.ALLOWED);
        return response.generateGrantedResponse(request,BigInteger.valueOf(System.nanoTime()).abs(),new Date()).getEncoded();
    }
}
