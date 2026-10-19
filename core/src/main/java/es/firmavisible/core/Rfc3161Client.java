package es.firmavisible.core;

import java.io.*;
import java.net.*;
import java.math.BigInteger;
import java.security.*;
import java.security.cert.*;
import java.util.*;
import org.bouncycastle.asn1.*;
import org.bouncycastle.asn1.cms.Attribute;
import org.bouncycastle.asn1.pkcs.PKCSObjectIdentifiers;
import org.bouncycastle.cert.X509CertificateHolder;
import org.bouncycastle.cert.jcajce.JcaX509CertificateConverter;
import org.bouncycastle.cms.SignerInformation;
import org.bouncycastle.cms.jcajce.JcaSimpleSignerInfoVerifierBuilder;
import org.bouncycastle.jce.provider.BouncyCastleProvider;
import org.bouncycastle.tsp.*;

/** RFC3161: nonce, imprint, signer/EKU, signature and pinned PKIX chain. No revocation or qualification assertion. */
public final class Rfc3161Client implements TimestampAuthority {
    public static final String ENDPOINT="http://timestamp.digicert.com";
    public interface Exchange { byte[] send(byte[] request) throws Exception; }
    private final Exchange exchange;
    private final Set<TrustAnchor> roots;
    public Rfc3161Client(Exchange exchange, Set<TrustAnchor> roots) {
        this.exchange=exchange; this.roots=new HashSet<>(roots);
        if(roots.isEmpty())throw new IllegalArgumentException("Falta la raíz de confianza de la autoridad de sellado.");
    }
    public static Rfc3161Client digicert() throws Exception {
        try(InputStream in=Rfc3161Client.class.getResourceAsStream("/tsa/DigiCertTrustedRootG4.cer")) {
            if(in==null)throw new IOException("Falta el certificado público de la autoridad de sellado.");
            X509Certificate root=(X509Certificate)CertificateFactory.getInstance("X.509").generateCertificate(in);
            return new Rfc3161Client(Rfc3161Client::post, Collections.singleton(new TrustAnchor(root,null)));
        }
    }
    private static byte[] post(byte[] request)throws Exception {
        // DigiCert's published RFC3161 endpoint is HTTP. Trust comes from the
        // nonce, signed token and pinned PKIX chain, never from this transport.
        HttpURLConnection connection=(HttpURLConnection)new URL(ENDPOINT).openConnection();
        connection.setInstanceFollowRedirects(false);connection.setConnectTimeout(15000);connection.setReadTimeout(20000);
        connection.setRequestMethod("POST");connection.setDoOutput(true);
        connection.setRequestProperty("Content-Type","application/timestamp-query");
        connection.setRequestProperty("Accept","application/timestamp-reply");
        connection.setFixedLengthStreamingMode(request.length);
        try {
            try(OutputStream out=connection.getOutputStream()){out.write(request);}
            if(connection.getResponseCode()!=200)throw new IOException("La autoridad de sellado no está disponible (HTTP "+connection.getResponseCode()+").");
            String type=connection.getContentType();
            if(type==null||!type.toLowerCase(Locale.ROOT).startsWith("application/timestamp-reply"))throw new IOException("Respuesta inesperada de la autoridad de sellado.");
            try(InputStream in=connection.getInputStream();ByteArrayOutputStream out=new ByteArrayOutputStream()) {
                byte[] buffer=new byte[8192];int n;
                while((n=in.read(buffer))!=-1){if(out.size()+n>128*1024)throw new IOException("Respuesta de sellado excesiva.");out.write(buffer,0,n);}return out.toByteArray();
            }
        } finally {connection.disconnect();}
    }
    @Override public byte[] timestamp(byte[] signature)throws Exception {
        TimeStampRequestGenerator generator=new TimeStampRequestGenerator();generator.setCertReq(true);
        byte[] imprint=MessageDigest.getInstance("SHA-256").digest(signature);
        TimeStampRequest request=generator.generate(TSPAlgorithms.SHA256,imprint,new BigInteger(160,new SecureRandom()));
        TimeStampResponse response=new TimeStampResponse(exchange.send(request.getEncoded()));
        response.validate(request);
        if(response.getStatus()!=0&&response.getStatus()!=1)throw new IOException("La autoridad ha rechazado el sello de tiempo.");
        TimeStampToken token=response.getTimeStampToken();
        if(token==null)throw new IOException("La respuesta no contiene sello de tiempo.");
        X509CertificateHolder signer=validate(token,signature);
        long difference=Math.abs(System.currentTimeMillis()-token.getTimeStampInfo().getGenTime().getTime());
        if(difference>10*60*1000L)throw new IOException("La hora del móvil difiere de la autoridad de sellado. Revisa el reloj y vuelve a intentarlo.");
        var converter=new JcaX509CertificateConverter().setProvider(new BouncyCastleProvider());
        X509CertSelector target=new X509CertSelector();target.setCertificate(converter.getCertificate(signer));
        PKIXBuilderParameters parameters=new PKIXBuilderParameters(roots,target);
        parameters.setDate(token.getTimeStampInfo().getGenTime());parameters.setRevocationEnabled(false);
        List<X509Certificate> certificates=new ArrayList<>();
        for(X509CertificateHolder holder:token.getCertificates().getMatches(null))certificates.add(converter.getCertificate(holder));
        parameters.addCertStore(CertStore.getInstance("Collection",new CollectionCertStoreParameters(certificates)));
        CertPathBuilder.getInstance("PKIX",new BouncyCastleProvider()).build(parameters);
        return token.getEncoded();
    }
    @SuppressWarnings("unchecked")
    public static X509CertificateHolder validate(TimeStampToken token, byte[] signature)throws Exception {
        var info=token.getTimeStampInfo();
        if(!info.getMessageImprintAlgOID().equals(TSPAlgorithms.SHA256)||!MessageDigest.isEqual(info.getMessageImprintDigest(),MessageDigest.getInstance("SHA-256").digest(signature)))
            throw new IOException("El sello de tiempo no corresponde a esta firma SHA-256.");
        Collection<X509CertificateHolder> matches=token.getCertificates().getMatches(token.getSID());
        if(matches.size()!=1)throw new IOException("No se identifica el certificado de la autoridad de sellado.");
        X509CertificateHolder signer=matches.iterator().next();
        token.validate(new JcaSimpleSignerInfoVerifierBuilder().setProvider(new BouncyCastleProvider()).build(signer));
        return signer;
    }
    public static Date verifyEmbedded(SignerInformation signer)throws Exception {
        if(signer.getUnsignedAttributes()==null)return null;
        var table=signer.getUnsignedAttributes();
        var values=table.getAll(PKCSObjectIdentifiers.id_aa_signatureTimeStampToken);
        if(values.size()==0)return null;
        if(values.size()!=1)throw new IOException("Múltiples sellos de tiempo en una misma firma.");
        Attribute attribute=Attribute.getInstance(values.get(0));
        if(attribute.getAttrValues().size()!=1)throw new IOException("Sello de tiempo mal formado.");
        TimeStampToken token=new TimeStampToken(org.bouncycastle.asn1.cms.ContentInfo.getInstance(attribute.getAttrValues().getObjectAt(0)));
        validate(token,signer.getSignature());return token.getTimeStampInfo().getGenTime();
    }
}
