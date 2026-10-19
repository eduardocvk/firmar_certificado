package es.firmavisible.core;

/** Returns a verified RFC3161 token bound to this CMS signature value. */
public interface TimestampAuthority {
    byte[] timestamp(byte[] signature) throws Exception;
}
