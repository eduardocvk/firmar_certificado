package es.firmavisible.pdf;

import es.firmavisible.core.*;
import es.firmavisible.core.Identity;
import es.firmavisible.test.TestTsa;
import java.io.*;
import java.nio.file.*;
import java.math.BigInteger;
import java.security.*;
import java.security.cert.*;
import java.util.*;
import java.awt.image.BufferedImage;
import org.apache.pdfbox.pdmodel.*;
import org.apache.pdfbox.pdmodel.common.PDRectangle;
import org.apache.pdfbox.rendering.PDFRenderer;
import org.bouncycastle.asn1.x500.X500Name;
import org.bouncycastle.cert.jcajce.*;
import org.bouncycastle.operator.jcajce.JcaContentSignerBuilder;
import org.bouncycastle.tsp.*;
import org.junit.jupiter.api.*;
import org.junit.jupiter.api.io.TempDir;
import static org.junit.jupiter.api.Assertions.*;

class TimestampAndAppearanceTest {
    @TempDir Path directory;
    private Identity identity()throws Exception {
        KeyPairGenerator generator=KeyPairGenerator.getInstance("RSA");generator.initialize(2048);var key=generator.generateKeyPair();
        var name=new X500Name("CN=Persona de prueba - NO REAL");var builder=new JcaX509v3CertificateBuilder(name,BigInteger.ONE,new Date(System.currentTimeMillis()-60000),new Date(System.currentTimeMillis()+86400000),name,key.getPublic());
        var certificate=new JcaX509CertificateConverter().getCertificate(builder.build(new JcaContentSignerBuilder("SHA256withRSA").build(key.getPrivate())));
        return new Identity(key.getPrivate(),new X509Certificate[]{certificate});
    }
    private File pdf(int rotation)throws Exception {
        File input=directory.resolve("original-"+rotation+".pdf").toFile();
        try(PDDocument doc=new PDDocument()){PDPage page=new PDPage(new PDRectangle(650,850));page.setCropBox(new PDRectangle(20,30,600,780));page.setRotation(rotation);doc.addPage(page);doc.save(input);}return input;
    }
    private byte[] image()throws Exception {
        BufferedImage image=new BufferedImage(180,60,BufferedImage.TYPE_INT_ARGB);var graphics=image.createGraphics();graphics.setColor(java.awt.Color.RED);graphics.fillRect(0,0,180,60);graphics.dispose();
        ByteArrayOutputStream out=new ByteArrayOutputStream();javax.imageio.ImageIO.write(image,"png",out);return out.toByteArray();
    }
    @Test void customTextImageAndTimestampPreserveIntegrityInAllRotations()throws Exception {
        Identity identity=identity();TestTsa tsa=new TestTsa();
        for(int rotation:new int[]{0,90,180,270}) {
            File source=pdf(rotation),output=directory.resolve("custom-"+rotation+".pdf").toFile();byte[] before=Files.readAllBytes(source.toPath());
            PdfEngine.sign(source,output,new Placement(0,.15f,.55f,.5f,.15f),identity,new SignatureAppearance("Revisado por {nombre}\nÁrea técnica · Málaga\n{fecha}",image(),true),tsa.client());
            var result=PdfEngine.verify(output);assertEquals(1,result.timestampedSignatures);assertNotNull(result.latestTimestamp);assertTrue(result.latestCoversWholeFile);assertArrayEquals(before,Files.readAllBytes(source.toPath()));
            try(PDDocument doc=PDDocument.load(output)) {
                var bitmap=new PDFRenderer(doc).renderImage(0);int red=0;
                for(int y=(int)(bitmap.getHeight()*.55);y<(int)(bitmap.getHeight()*.7);y++)for(int x=(int)(bitmap.getWidth()*.15);x<(int)(bitmap.getWidth()*.65);x++){int c=bitmap.getRGB(x,y);if(((c>>16)&255)>220&&((c>>8)&255)<40&&(c&255)<40)red++;}
                assertTrue(red>100,"Imagen visible en rotación "+rotation);
                var widget=doc.getPage(0).getAnnotations().get(0);assertTrue(widget.getAppearance().getNormalAppearance().getAppearanceStream().getResources().getXObjectNames().iterator().hasNext());
            }
        }
    }
    @Test void imageOnlyTransparentAndLongTextAreHandledWithoutTruncation()throws Exception {
        File source=pdf(0),output=directory.resolve("image-only.pdf").toFile();
        PdfEngine.sign(source,output,new Placement(0,.1f,.5f,.5f,.1f),identity(),new SignatureAppearance("",image(),false),null);assertEquals(0,PdfEngine.verify(output).timestampedSignatures);
        assertThrows(IllegalArgumentException.class,()->new SignatureAppearance("",null,false));
        assertThrows(Exception.class,()->PdfEngine.sign(source,directory.resolve("too-long.pdf").toFile(),new Placement(0,.1f,.5f,.18f,.06f),identity(),new SignatureAppearance("Texto largo ".repeat(49),null,true),null));
        assertFalse(Files.exists(directory.resolve("too-long.pdf")));
    }
    @Test void transparentAppearancePreservesColoredPageAndUsesChosenTextSize()throws Exception {
        File source=directory.resolve("colored.pdf").toFile(),output=directory.resolve("transparent.pdf").toFile();
        try(PDDocument doc=new PDDocument()){PDPage page=new PDPage(PDRectangle.A4);doc.addPage(page);try(PDPageContentStream cs=new PDPageContentStream(doc,page)){cs.setNonStrokingColor(.2f,.6f,.8f);cs.addRect(0,0,600,850);cs.fill();}doc.save(source);}
        PdfEngine.sign(source,output,new Placement(0,.1f,.5f,.6f,.2f),identity(),new SignatureAppearance("Texto pequeño",image(),true,6),null);
        try(PDDocument doc=PDDocument.load(output)){
            BufferedImage bitmap=new PDFRenderer(doc).renderImage(0);java.awt.Color pixel=new java.awt.Color(bitmap.getRGB((int)(bitmap.getWidth()*.12),(int)(bitmap.getHeight()*.52)));
            assertTrue(Math.abs(pixel.getRed()-51)<3&&Math.abs(pixel.getGreen()-153)<3&&Math.abs(pixel.getBlue()-204)<3,"El recuadro tapa el contenido de la página");
            var appearance=doc.getPage(0).getAnnotations().get(0).getAppearance().getNormalAppearance().getAppearanceStream();
            try(InputStream stream=appearance.getContents()){String commands=new String(stream.readAllBytes(),java.nio.charset.StandardCharsets.ISO_8859_1);assertTrue(commands.matches("(?s).*\\s6\\s+Tf.*"),"El PDF no usa los 6 pt seleccionados");}
        }
        assertTrue(PdfEngine.verify(output).latestCoversWholeFile);
        assertFalse(SignatureAppearance.standard().decorated);
        assertThrows(IllegalArgumentException.class,()->new SignatureAppearance("Texto",null,false,Float.NaN));
        assertThrows(IllegalArgumentException.class,()->new SignatureAppearance("Texto",null,false,19));
    }
    @Test void rejectsWrongNonceWrongImprintAndUntrustedAuthority()throws Exception {
        TestTsa authority=new TestTsa();byte[] signature=new byte[]{1,2,3};
        var roots=Collections.singleton(new TrustAnchor(authority.root,null));
        assertThrows(Exception.class,()->new Rfc3161Client(bytes->{var original=new TimeStampRequest(bytes);var g=new TimeStampRequestGenerator();g.setCertReq(true);return authority.respond(g.generate(TSPAlgorithms.SHA256,original.getMessageImprintDigest(),original.getNonce().add(BigInteger.ONE)));},roots).timestamp(signature));
        assertThrows(Exception.class,()->new Rfc3161Client(bytes->{var original=new TimeStampRequest(bytes);var g=new TimeStampRequestGenerator();g.setCertReq(true);return authority.respond(g.generate(TSPAlgorithms.SHA256,new byte[32],original.getNonce()));},roots).timestamp(signature));
        TestTsa other=new TestTsa();assertThrows(Exception.class,()->new Rfc3161Client(bytes->authority.respond(new TimeStampRequest(bytes)),Collections.singleton(new TrustAnchor(other.root,null))).timestamp(signature));
    }
    @Test void timestampFailureNeverProducesAnUnstampedResult()throws Exception {
        File output=directory.resolve("failed.pdf").toFile();
        assertThrows(Exception.class,()->PdfEngine.sign(pdf(0),output,new Placement(0,.1f,.5f,.5f,.1f),identity(),SignatureAppearance.standard(),signature->{throw new IOException("Sin conexión");}));assertFalse(output.exists());
    }
    @Test void realDigicertTimestampWhenExplicitlyEnabled()throws Exception {
        Assumptions.assumeTrue("true".equals(System.getenv("FIRMAVISIBLE_LIVE_TSA")));
        File output=directory.resolve("real-timestamp.pdf").toFile();
        PdfEngine.sign(pdf(0),output,new Placement(0,.1f,.5f,.5f,.12f),identity(),SignatureAppearance.standard(),Rfc3161Client.digicert());
        assertEquals(1,PdfEngine.verify(output).timestampedSignatures);
        Files.createDirectories(Paths.get("build/test-artifacts"));Files.copy(output.toPath(),Paths.get("build/test-artifacts/real-timestamp.pdf"),StandardCopyOption.REPLACE_EXISTING);
        try(PDDocument doc=PDDocument.load(output)) {
            byte[] pdf=Files.readAllBytes(output.toPath());var signature=doc.getSignatureDictionaries().get(0);
            byte[] cmsBytes;
            try(var asn=new org.bouncycastle.asn1.ASN1InputStream(signature.getContents(pdf))){cmsBytes=asn.readObject().getEncoded();}
            var cms=new org.bouncycastle.cms.CMSSignedData(new org.bouncycastle.cms.CMSProcessableByteArray(signature.getSignedContent(pdf)),cmsBytes);
            var signer=cms.getSignerInfos().getSigners().iterator().next();
            var value=signer.getUnsignedAttributes().get(org.bouncycastle.asn1.pkcs.PKCSObjectIdentifiers.id_aa_signatureTimeStampToken).getAttrValues().getObjectAt(0);
            var token=new TimeStampToken(org.bouncycastle.asn1.cms.ContentInfo.getInstance(value));
            Files.write(Paths.get("build/test-artifacts/timestamp.der"),token.getEncoded());Files.write(Paths.get("build/test-artifacts/signature-value.bin"),signer.getSignature());
            try(var out=new org.bouncycastle.util.io.pem.PemWriter(Files.newBufferedWriter(Paths.get("build/test-artifacts/tsa-chain.pem")))) {
                for(var certificate:token.getCertificates().getMatches(null))out.writeObject(new org.bouncycastle.util.io.pem.PemObject("CERTIFICATE",certificate.getEncoded()));
            }
        }
    }
}
