package es.firmavisible.core;

import java.io.*;
import java.security.MessageDigest;
import java.util.*;
import org.bouncycastle.asn1.*;
import org.bouncycastle.asn1.cms.*;
import org.bouncycastle.asn1.ess.*;
import org.bouncycastle.asn1.pkcs.PKCSObjectIdentifiers;
import org.bouncycastle.cert.jcajce.JcaCertStore;
import org.bouncycastle.cms.*;
import org.bouncycastle.cms.jcajce.*;
import org.bouncycastle.jce.provider.BouncyCastleProvider;
import org.bouncycastle.operator.jcajce.*;

/** Detached CAdES CMS used by PDF's ETSI.CAdES.detached subfilter. */
public final class CmsSigner {
    private CmsSigner() {}
    public static byte[] sign(InputStream input, Identity identity) throws Exception {
        return sign(input, identity, null);
    }
    public static byte[] sign(InputStream input, Identity identity, TimestampAuthority tsa) throws Exception {
        identity.checkUsable();
        var certHash = MessageDigest.getInstance("SHA-256").digest(identity.certificate().getEncoded());
        var attributes = new Hashtable<ASN1ObjectIdentifier, Attribute>();
        attributes.put(PKCSObjectIdentifiers.id_aa_signingCertificateV2,
            new Attribute(PKCSObjectIdentifiers.id_aa_signingCertificateV2,
                new DERSet(new SigningCertificateV2(new ESSCertIDv2(certHash)))));
        var attrGenerator = new DefaultSignedAttributeTableGenerator(new AttributeTable(attributes)) {
            @Override public AttributeTable getAttributes(Map parameters) {
                // PAdES records the claimed time in the PDF /M entry, not CMS signingTime.
                return super.getAttributes(parameters).remove(CMSAttributes.signingTime);
            }
        };
        var digestProvider = new JcaDigestCalculatorProviderBuilder().setProvider(new BouncyCastleProvider()).build();
        var signer = new JcaContentSignerBuilder(identity.algorithm()).build(identity.key());
        var generator = new CMSSignedDataGenerator();
        generator.addSignerInfoGenerator(new JcaSignerInfoGeneratorBuilder(digestProvider)
            .setSignedAttributeGenerator(attrGenerator).build(signer, identity.certificate()));
        generator.addCertificates(new JcaCertStore(Arrays.asList(identity.chain())));
        CMSTypedData content = new CMSTypedData() {
            public ASN1ObjectIdentifier getContentType() { return CMSObjectIdentifiers.data; }
            public Object getContent() { return input; }
            public void write(OutputStream out) throws IOException { byte[] b = new byte[32768]; int n; while ((n = input.read(b)) != -1) out.write(b, 0, n); }
        };
        CMSSignedData signed=generator.generate(content, false);
        if(tsa!=null) {
            List<SignerInformation> stamped=new ArrayList<>();
            for(SignerInformation info:signed.getSignerInfos().getSigners()) {
                byte[] token=tsa.timestamp(info.getSignature());
                var timestamp=new Attribute(PKCSObjectIdentifiers.id_aa_signatureTimeStampToken,new DERSet(ASN1Primitive.fromByteArray(token)));
                var table=new AttributeTable(timestamp);
                SignerInformation updated=SignerInformation.replaceUnsignedAttributes(info,table);
                Rfc3161Client.verifyEmbedded(updated);
                stamped.add(updated);
            }
            signed=CMSSignedData.replaceSigners(signed,new SignerInformationStore(stamped));
        }
        return signed.getEncoded();
    }
}
